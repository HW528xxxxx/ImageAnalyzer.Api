using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ComputerVision.Services
{
    public class LineNotifyService
    {
        private readonly HttpClient _http;
        private readonly string _channelAccessToken;
        private readonly string _userId;

        public LineNotifyService(
            IHttpClientFactory httpFactory,
            string channelAccessToken,
            string userId)
        {
            _http = httpFactory.CreateClient();
            _channelAccessToken = channelAccessToken;
            _userId = userId;
        }

        public async Task SendMessageAsync(string text)
        {
            var payload = new
            {
                to = _userId,
                messages = new[]
                {
                    new { type = "text", text }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push")
            {
                Content = JsonContent.Create(payload)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _channelAccessToken);

            using var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"LINE 傳送失敗 ({(int)response.StatusCode}): {body}");
            }
        }
    }
}
