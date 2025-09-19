using OpenAI.Chat; // 2.x SDK
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using System.Diagnostics;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;
using System.Text.RegularExpressions;


public class AzureImageAnalyzer : IImageAnalyzer
{
    private readonly ComputerVisionClient _cv;
    private readonly ChatClient _chatClient;

    public AzureImageAnalyzer(ComputerVisionClient cv, ChatClient chatClient)
    {
        _cv = cv;
        _chatClient = chatClient;
    }

    public async Task<ImageAnalysisResult> AnalyzeAsync(byte[] bytes)
    {
        var stopwatch = Stopwatch.StartNew();

        // Computer Vision 分析
        var features = new List<VisualFeatureTypes?>
            { VisualFeatureTypes.Description, VisualFeatureTypes.Tags, VisualFeatureTypes.Objects };
        using var ms1 = new MemoryStream(bytes);
        var analysis = await _cv.AnalyzeImageInStreamAsync(ms1, features);

        // OCR
        var ocrLines = await ReadOcrAsync(bytes);

        // GPT multimodal
        var gptResult = await AnalyzeWithOpenAIAsync(analysis, ocrLines, bytes);

        stopwatch.Stop();

        return new ImageAnalysisResult
        {
            Tags = analysis.Tags?.Where(t => t.Confidence > 0.88).Select(t => (t.Name, t.Confidence)) ?? Enumerable.Empty<(string, double)>(),
            Objects = analysis.Objects?.Select(o => (o.ObjectProperty, o.Confidence)) ?? Enumerable.Empty<(string, double)>(),
            Caption = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault()?.Text,
            CaptionConfidence = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault()?.Confidence,
            OcrLines = ocrLines,
            GptDescription = gptResult,
            RequestDurationMs = stopwatch.ElapsedMilliseconds / 1000.0
        };
    }

    private async Task<List<string>> ReadOcrAsync(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var readOp = await _cv.ReadInStreamAsync(ms);
        var opId = readOp.OperationLocation.Split('/').Last();

        ReadOperationResult readResult;
        do
        {
            readResult = await _cv.GetReadResultAsync(Guid.Parse(opId));
            await Task.Delay(500);
        } while (readResult.Status == OperationStatusCodes.Running || readResult.Status == OperationStatusCodes.NotStarted);

        var lines = new List<string>();
        if (readResult.Status == OperationStatusCodes.Succeeded)
        {
            foreach (var page in readResult.AnalyzeResult.ReadResults)
                foreach (var line in page.Lines)
                    lines.Add(line.Text);
        }

        return lines;
    }

    private async Task<GptResult?> AnalyzeWithOpenAIAsync(ImageAnalysis analysis, List<string> ocrLines, byte[] bytes)
    {
        // ImageSharp 縮圖 + Base64
        using var image = SixLabors.ImageSharp.Image.Load(bytes);
        int maxSize = 256;
        int width = image.Width > image.Height ? maxSize : image.Width * maxSize / image.Height;
        int height = image.Height >= image.Width ? maxSize : image.Height * maxSize / image.Width;
        image.Mutate(x => x.Resize(width, height));

        using var msThumb = new MemoryStream();
        var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 70 };
        image.Save(msThumb, encoder);
        string base64Image = Convert.ToBase64String(msThumb.ToArray());
        string imageDataUri = $"data:image/jpeg;base64,{base64Image}";

        string caption = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault()?.Text;
        var tags = analysis.Tags?.Where(t => t.Confidence > 0.88).Select(t => t.Name) ?? Enumerable.Empty<string>();

        string prompt = $@"
我有一張圖片，Azure Computer Vision 的分析結果如下：
- Caption: {caption}
- Tags: {string.Join(", ", tags)}
- OCR: {string.Join(" | ", ocrLines)}

請幫我給出更精準的描述，用中文描述：
1. 如果能判斷品牌或型號（例如 Tesla Cybertruck），請直接指出。
2. 如果是電動車，請加上 '電動車' 標籤。
3. 回傳 JSON 格式：{{ ""description"": ..., ""extraTags"": [...] }}
";

        var chatMessages = new List<ChatMessage>()
        {
            new SystemChatMessage("你是一個影像辨識專家"),
            new UserChatMessage(new List<ChatMessageContentPart>
            {
                ChatMessageContentPart.CreateTextPart(prompt),
                ChatMessageContentPart.CreateImagePart(new Uri(imageDataUri))
            })
        };

        var completion = await _chatClient.CompleteChatAsync(chatMessages);
        string gptResult = completion.Value.Content[0].Text.Replace("```json", "").Replace("```", "").Trim();
        var match = Regex.Match(gptResult, "{.*}", RegexOptions.Singleline);
        if (match.Success) return JsonSerializer.Deserialize<GptResult>(match.Value);
        return null;
    }
}
