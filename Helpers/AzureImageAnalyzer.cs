using OpenAI.Chat; // 2.x SDK
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using System.Diagnostics;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;
using System.Text.RegularExpressions;
using ComputerVision.Dto;
using ComputerVision.Exceptions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;

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
        ImageAnalysis analysis;

        try
        {
            // Computer Vision 分析
            var features = new List<VisualFeatureTypes?>
                { VisualFeatureTypes.Description, VisualFeatureTypes.Tags, VisualFeatureTypes.Objects };
            using var ms1 = new MemoryStream(bytes);
            analysis = await _cv.AnalyzeImageInStreamAsync(ms1, features);
        }
        catch (Exception ex)
        {
            throw new AnalyzerException(
                MessageCodeEnum.ComputerVisionFailed,
                EnumHelper.GetEnumDescription(MessageCodeEnum.ComputerVisionFailed),
                ex
            );
        }

        List<string> ocrLines;
        try
        {
            // OCR
            ocrLines = await ReadOcrAsync(bytes);
        }
        catch (Exception ex)
        {
            throw new AnalyzerException(
                MessageCodeEnum.OcrFailed,
                EnumHelper.GetEnumDescription(MessageCodeEnum.OcrFailed),
                ex
            );
        }

        GptResult? gptResult;
        try
        {
            // GPT multimodal
            gptResult = await AnalyzeWithOpenAIAsync(analysis, ocrLines, bytes);
        }
        catch (Exception ex)
        {
            throw new AnalyzerException(
                MessageCodeEnum.OpenAiFailed,
                EnumHelper.GetEnumDescription(MessageCodeEnum.OpenAiFailed),
                ex
            );
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

        double combinedCaptionConfidence = 0.25 * cvCaptionConfidence + 0.5 * tagsConfidence + 0.25 * objectsConfidence;

        // === 新增可視化標註框 ===
        string annotatedImageBase64 = DrawBoundingBoxes(bytes, analysis);

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
            RequestDurationMs = stopwatch.ElapsedMilliseconds / 1000.0,
            AnnotatedImageBase64 = annotatedImageBase64
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
        try
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
    1. 如果能判斷品牌或型號（例如 Tesla Cybertruck 或 7-ELEVEN），請直接指出並補充該品牌的特色。
    2. 請先推論出圖片最主要的類別（例如：便利商店、咖啡店、速食店、電動車等），並將它放在 extraTags 的第一個位置。
    3. 再根據這個主要類別，補充更多細節（例如營業型態、常見特色、商品或服務）。
    4. 回傳 JSON 格式：{{ ""description"": ..., ""extraTags"": [...] }}
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
        catch (Exception ex)
        {
            throw new AnalyzerException(
                MessageCodeEnum.OpenAiFailed,
                EnumHelper.GetEnumDescription(MessageCodeEnum.OpenAiFailed),
                ex
            );
        }
    }

    private string DrawBoundingBoxes(byte[] imageBytes, ImageAnalysis analysis)
    {
        using var image = Image.Load<Rgba32>(imageBytes);
        var font = SystemFonts.CreateFont("Arial", 26, FontStyle.Bold);
        var pen = new SolidPen(Color.Red, 4);

        var tags = analysis.Tags ?? new List<ImageTag>();var objects = analysis.Objects ?? Enumerable.Empty<DetectedObject>();

        if (objects.Any())
        {
            // 有偵測到物件
            foreach (var obj in objects)
            {
                var rect = new Rectangle(obj.Rectangle.X, obj.Rectangle.Y, obj.Rectangle.W, obj.Rectangle.H);
                string label = obj.ObjectProperty;
                double confidence = obj.Confidence;

                var topTag = tags
                    .Where(t =>
                        t.Confidence > confidence &&
                        (
                            t.Name.Contains(label, StringComparison.OrdinalIgnoreCase) ||
                            label.Contains(t.Name, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    .OrderByDescending(t => t.Confidence)
                    .FirstOrDefault();

                if (topTag != null)
                {
                    label = topTag.Name;
                    confidence = topTag.Confidence;
                }
                else
                {
                    var fallbackTag = tags.OrderByDescending(t => t.Confidence).FirstOrDefault();
                    if (fallbackTag != null && fallbackTag.Confidence > confidence)
                    {
                        label = fallbackTag.Name;
                        confidence = fallbackTag.Confidence;
                    }
                }

                string labelText = $"{label} ({confidence:P0})";

                image.Mutate(x =>
                {
                    x.Draw(pen, rect);
                    x.DrawText(labelText, font, Color.Yellow, new PointF(rect.X, Math.Max(0, rect.Y - 32)));
                });
            }
        }
        else
        {
            // 沒有偵測到物件
            string message;

            if (analysis.Description?.Captions?.Any() == true)
            {
                message = analysis.Description.Captions.First().Text;
            }
            else if (tags.Any())
            {
                message = "Tags: " + string.Join(", ", tags.Select(t => t.Name));
            }
            else
            {
                message = "未偵測到任何物件";
            }

            image.Mutate(x =>
            {
                x.DrawText(message, font, Color.Yellow, new PointF(20, 20));
            });
        }

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        string base64 = Convert.ToBase64String(ms.ToArray());
        return "data:image/jpeg;base64," + base64;
    }

}
