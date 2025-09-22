using OpenAI.Chat; // 2.x SDK
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using System.Diagnostics;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;
using System.Text.RegularExpressions;
using ComputerVision.Dto;
using ComputerVision.Interface;


public class AzureImageAnalyzer : IImageAnalyzer
{
    private readonly ComputerVisionClient _cv;
    private readonly ChatClient _chatClient;
    private readonly IVideoIndexerAnalyzer _videoIndexer;

    public AzureImageAnalyzer(
        ComputerVisionClient cv,
        ChatClient chatClient,
        IVideoIndexerAnalyzer videoIndexer)
    {
        _cv = cv;
        _chatClient = chatClient;
        _videoIndexer = videoIndexer;
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

        // 取得 Video Indexer 的人物辨識結果
        IList<PersonInfoDto> people;
        try
        {
            people = await _videoIndexer.RecognizePeopleAsync(bytes);
        }
        catch (Exception ex)
        {
            // 可視情況記錄錯誤並回傳空結果，不要讓整個分析失敗
            people = new List<PersonInfoDto>();
            // Log.Error(ex, "Video Indexer failed");
        }

        stopwatch.Stop();

        // 多模型融合計算 CaptionConfidence
        var cvCaption = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault();
        double cvCaptionConfidence = cvCaption?.Confidence ?? 0;

        double tagsConfidence = 0;
        if (analysis.Tags != null && analysis.Tags.Any())
            tagsConfidence = analysis.Tags
                .OrderByDescending(t => t.Confidence)
                .Take(5)
                .Average(t => t.Confidence);

        double objectsConfidence = 0;
        if (analysis.Objects != null && analysis.Objects.Any())
            objectsConfidence = analysis.Objects
                .OrderByDescending(o => o.Confidence)
                .Take(5)
                .Average(o => o.Confidence);

        double combinedCaptionConfidence = 0.5 * cvCaptionConfidence + 0.25 * tagsConfidence + 0.25 * objectsConfidence;

        return new ImageAnalysisResult
        {
            Tags = analysis.Tags?
            .Where(t => t.Confidence > 0.88)
            .Select(t => new ObjectInfo { Name = t.Name, Confidence = t.Confidence }) 
            ?? Enumerable.Empty<ObjectInfo>(),

            Objects = analysis.Objects?
            .Select(o => new ObjectInfo { Name = o.ObjectProperty, Confidence = o.Confidence }) 
            ?? Enumerable.Empty<ObjectInfo>(),

            Caption = cvCaption?.Text,
            CaptionConfidence = combinedCaptionConfidence, // ← 使用加權平均後的信心值
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
