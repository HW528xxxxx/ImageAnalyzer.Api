using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ComputerVision.Interface;

public class AzureOpenAiTtsService : ITtsService
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _key;
    private readonly string _deployment;
    private readonly string _apiVersion;

    public AzureOpenAiTtsService(
        IHttpClientFactory httpFactory,
        string endpoint,
        string key,
        string deployment,
        string apiVersion)
    {
        _http = httpFactory.CreateClient();
        _endpoint = endpoint;
        _key = key;
        _deployment = deployment;
        _apiVersion = apiVersion;
    }

    public async Task<string> TextToSpeechBase64Async(string text, string voice = "alloy", string format = "mp3")
    {
        var url = $"{_endpoint}/openai/deployments/{_deployment}/audio/speech?api-version={_apiVersion}";

        var payload = new
        {
            model = _deployment,
            input = text,
            voice = voice,
            format = format
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception($"Azure OpenAI TTS 失敗 ({(int)response.StatusCode}): {body}");
        }

        var audioBytes = await response.Content.ReadAsByteArrayAsync();
        return Convert.ToBase64String(audioBytes);
    }
}
