using System.Text.Json;
using ComputerVision.Dto;
using ComputerVision.Interface;

namespace ComputerVision.Helpers
{
    public class AzureVideoIndexerAnalyzer : IVideoIndexerAnalyzer, IDisposable
    {
        private readonly string _accountId;
        private readonly string _location;
        private readonly HttpClient _http;

        public AzureVideoIndexerAnalyzer(string accountId, string location, HttpClient? httpClient = null)
        {
            _accountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
            _location = location ?? throw new ArgumentNullException(nameof(location));
            _http = httpClient ?? new HttpClient();
        }

        // 自動取得 AccessToken
        private async Task<string> GetAccessTokenAsync()
        {
            var url = $"https://api.videoindexer.ai/Auth/{_location}/Accounts/{_accountId}/AccessToken?allowEdit=true";
            var resp = await _http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync();
        }

        public async Task<IList<PersonInfoDto>> RecognizePeopleAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
        {
            var token = await GetAccessTokenAsync();

            // 1) Upload image as a "video"
            using var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(imageBytes), "file", "image.jpg");

            var uploadUrl = $"https://api.videoindexer.ai/{_location}/Accounts/{_accountId}/Videos?name=tempImage&accessToken={Uri.EscapeDataString(token)}";
            var uploadResponse = await _http.PostAsync(uploadUrl, content, cancellationToken);
            uploadResponse.EnsureSuccessStatusCode();
            var uploadJson = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(uploadJson);
            string? videoId = TryGetString(doc.RootElement, "id") ?? TryGetString(doc.RootElement, "videoId");
            if (string.IsNullOrEmpty(videoId))
                throw new InvalidOperationException("Video upload did not return video id.");

            // 2) Poll Index (等待分析完成)
            var indexUrl = $"https://api.videoindexer.ai/{_location}/Accounts/{_accountId}/Videos/{videoId}/Index?accessToken={Uri.EscapeDataString(token)}";

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

                var idxResp = await _http.GetAsync(indexUrl, cancellationToken);
                idxResp.EnsureSuccessStatusCode();
                var idxJson = await idxResp.Content.ReadAsStringAsync(cancellationToken);

                using var idxDoc = JsonDocument.Parse(idxJson);

                string? state = TryGetString(idxDoc.RootElement, "state")
                               ?? TryGetStringIn(idxDoc.RootElement, "videos", 0, "state");

                if (!string.IsNullOrEmpty(state) && state.Equals("Processed", StringComparison.OrdinalIgnoreCase))
                {
                    var facesElement = TryGetElement(idxDoc.RootElement, "videos", 0, "insights", "faces");
                    var list = new List<PersonInfoDto>();

                    if (facesElement.HasValue && facesElement.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var f in facesElement.Value.EnumerateArray())
                        {
                            var name = TryGetString(f, "name") ?? TryGetString(f, "id") ?? "Unknown";
                            var faceId = TryGetString(f, "id");
                            double? confidence = null;

                            var instances = TryGetElement(f, "instances");
                            if (instances.HasValue && instances.Value.ValueKind == JsonValueKind.Array)
                            {
                                var firstInstance = instances.Value.EnumerateArray().FirstOrDefault();
                                if (firstInstance.ValueKind != JsonValueKind.Undefined && firstInstance.ValueKind == JsonValueKind.Object)
                                {
                                    if (firstInstance.TryGetProperty("confidence", out var confEl) && confEl.TryGetDouble(out var confD))
                                        confidence = confD;
                                }
                            }

                            list.Add(new PersonInfoDto
                            {
                                Name = name,
                                FaceId = faceId,
                                Confidence = confidence
                            });
                        }
                    }

                    return list;
                }

                if (!string.IsNullOrEmpty(state) && state.Equals("Failed", StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<PersonInfoDto>();
            }

            return Array.Empty<PersonInfoDto>();
        }

        private static string? TryGetString(JsonElement el, string propName)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(propName, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }

        private static string? TryGetStringIn(JsonElement el, string arrName, int index, string propName)
        {
            var e = TryGetElement(el, arrName, index);
            if (e.HasValue)
                return TryGetString(e.Value, propName);
            return null;
        }

        private static OptionalElement TryGetElement(JsonElement el, params object[] path)
        {
            JsonElement cur = el;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] is string key)
                {
                    if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(key, out var next))
                        return OptionalElement.Empty;
                    cur = next;
                }
                else if (path[i] is int idx)
                {
                    if (cur.ValueKind != JsonValueKind.Array)
                        return OptionalElement.Empty;
                    var arr = cur.EnumerateArray().ToArray();
                    if (idx < 0 || idx >= arr.Length)
                        return OptionalElement.Empty;
                    cur = arr[idx];
                }
                else
                {
                    return OptionalElement.Empty;
                }
            }

            return new OptionalElement(cur);
        }

        public void Dispose()
        {
            _http?.Dispose();
        }

        private readonly struct OptionalElement
        {
            public readonly JsonElement Value;
            public readonly bool HasValue;

            private OptionalElement(bool hasValue, JsonElement value)
            {
                HasValue = hasValue;
                Value = value;
            }

            public OptionalElement(JsonElement value) : this(true, value) { }

            public static OptionalElement Empty => new OptionalElement(false, default);
        }
    }
}
