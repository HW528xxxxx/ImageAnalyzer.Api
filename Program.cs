using Azure;
using OpenAI.Chat; // 2.x SDK
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.AspNetCore.Mvc;
using Azure.AI.OpenAI;
using System.Diagnostics;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
// ----------------- CORS -----------------
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("frontend", p => p
        .WithOrigins("http://localhost:5173", "http://localhost:8080")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// ----------------- 上傳大小限制 -----------------
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20_000_000);

// ----------------- Azure Computer Vision -----------------
var endpoint = builder.Configuration["AzureVision:Endpoint"]!;
var key      = builder.Configuration["AzureVision:Key"]!;

builder.Services.AddSingleton(new ComputerVisionClient(
    new ApiKeyServiceClientCredentials(key)) { Endpoint = endpoint });

// ----------------- Azure OpenAI (2.x) -----------------
var aoaiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"]!;
var aoaiKey      = builder.Configuration["AzureOpenAI:Key"]!;
var deployName   = builder.Configuration["AzureOpenAI:Deployment"]!;

var chatClient = new AzureOpenAIClient(
    new Uri(aoaiEndpoint),
    new System.ClientModel.ApiKeyCredential(aoaiKey))
    .GetChatClient(deployName);

builder.Services.AddSingleton(chatClient);

// ----------------- Swagger -----------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("frontend");

// ----------------- Minimal API Endpoint (Computer Vision + Azure OpenAI 2.x) -----------------
app.MapPost("/api/analyze", async (HttpContext context,
    [FromServices] ComputerVisionClient cv,
    [FromServices] ChatClient client) =>
{
    try
    {
        var req = context.Request;
        if (!req.HasFormContentType)
            return Results.BadRequest(new ErrorResponse
            {
                Code = "InvalidRequest",
                Message = "請使用 multipart/form-data 上傳"
            });

        var form = await req.ReadFormAsync();
        var file = form.Files["file"]; // Postman 裡的檔案欄位名稱

        if (file == null || file.Length == 0)
            return Results.BadRequest(new ErrorResponse
            {
                Code = "FileMissing",
                Message = "請上傳圖片檔"
            });

        // 將檔案裝進記憶體，避免後面要重複讀兩次（Analyze + OCR）
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        var stopwatch = Stopwatch.StartNew();

    // Parallelize: Image Analysis + OCR 同時發起
    var features = new List<VisualFeatureTypes?>()
        { VisualFeatureTypes.Description, VisualFeatureTypes.Tags, VisualFeatureTypes.Objects };

    using var ms1 = new MemoryStream(bytes);
    var analyzeTask = cv.AnalyzeImageInStreamAsync(ms1, features);

    using var ms2 = new MemoryStream(bytes);
    var readTask = cv.ReadInStreamAsync(ms2);

    await Task.WhenAll(analyzeTask, readTask);

    var analysis = analyzeTask.Result;

    // OCR 輪詢
    var readOp = readTask.Result;
    var opId = readOp.OperationLocation.Split('/').Last();
    ReadOperationResult readResult;
    do
    {
        readResult = await cv.GetReadResultAsync(Guid.Parse(opId));
        await Task.Delay(500);
    } while (readResult.Status == OperationStatusCodes.Running ||
             readResult.Status == OperationStatusCodes.NotStarted);

    var ocrLines = new List<string>();
    if (readResult.Status == OperationStatusCodes.Succeeded)
    {
        foreach (var page in readResult.AnalyzeResult.ReadResults)
            foreach (var line in page.Lines)
                ocrLines.Add(line.Text);
    }

    // ----------------- 呼叫 Azure OpenAI 2.x -----------------
    var tags = analysis.Tags?
    .Where(t => t.Confidence > 0.88)  // 只取信心值大於 88%
    .Select(t => t.Name)
    ?? Enumerable.Empty<string>();
    var caption = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault()?.Text;

    // ---------------------- ImageSharp 縮圖 + Base64 Data URI ----------------------
    using var image = SixLabors.ImageSharp.Image.Load(bytes); // 載入圖片
    int maxSize = 256; // 縮小尺寸
    int width = image.Width > image.Height ? maxSize : image.Width * maxSize / image.Height;
    int height = image.Height >= image.Width ? maxSize : image.Height * maxSize / image.Width;

    image.Mutate(x => x.Resize(width, height)); // 縮圖

    using var msThumb = new MemoryStream();
    var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
    {
        Quality = 70 // 降低 JPEG 質量，減少 payload (傳送的資料量)
    };
    image.Save(msThumb, encoder); // 輸出 JPEG
    string base64Image = Convert.ToBase64String(msThumb.ToArray());
    string imageDataUri = $"data:image/jpeg;base64,{base64Image}";

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

    // 用 multimodal message（文字 + 圖片）
    var chatMessages = new List<ChatMessage>()
    {
        new SystemChatMessage("你是一個影像辨識專家"),
        new UserChatMessage(new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(prompt),
            ChatMessageContentPart.CreateImagePart(new Uri(imageDataUri))
        })
    };

    var completion = await client.CompleteChatAsync(chatMessages);

    // 清理 GPT 回傳，去掉 ```json、``` 和多餘換行
    string gptResult = completion.Value.Content[0].Text
        .Replace("```json", "")
        .Replace("```", "")
        .Trim();

    // 用 Regex 找出 { 開頭到 } 結尾的 JSON
    var match = Regex.Match(gptResult, "{.*}", RegexOptions.Singleline);
    GptResult? gptJson = null;
    if (match.Success)
    {
        gptJson = JsonSerializer.Deserialize<GptResult>(match.Value);
    }

    stopwatch.Stop();
        double elapsedSeconds = stopwatch.ElapsedMilliseconds / 1000.0;

        return Results.Ok(new
        {
            tags = analysis.Tags?
                .Where(t => t.Confidence > 0.88)
                .Select(t => new { t.Name, t.Confidence }) ?? Enumerable.Empty<object>(),
            objects = analysis.Objects?.Select(o => new { Name = o.ObjectProperty, o.Confidence }) ?? Enumerable.Empty<object>(),
            caption = caption,
            captionConfidence = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault()?.Confidence,
            ocr = ocrLines,
            gptDescription = gptJson,
            requestDurationMs = elapsedSeconds
        });
    }
    catch (ComputerVisionOcrErrorException ex)
    {
        return Results.BadRequest(new ErrorResponse
        {
            Code = MessageCodeEnum.OcrFailed.ToString(),
            Message = $"{EnumHelper.GetEnumDescription(MessageCodeEnum.OcrFailed)}: {ex.Message}",

        });
    }
    catch (SixLabors.ImageSharp.UnknownImageFormatException ex)
    {
        return Results.BadRequest(new ErrorResponse
        {
            Code = MessageCodeEnum.ImageFormatError.ToString(),
            Message = $"{EnumHelper.GetEnumDescription(MessageCodeEnum.ImageFormatError)}: {ex.Message}"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(new ErrorResponse
        {
            Code = MessageCodeEnum.非預期系統錯誤.ToString(),
            Message = $"{EnumHelper.GetEnumDescription(MessageCodeEnum.ServerError)}: {ex.Message}"
        }.ToString(), statusCode: 500);
    }
})
.AllowAnonymous(); // <- 不需要 Anti-Forgery

// ----------------- Minimal API Endpoint (Azure OpenAI 2.x Test) -----------------
app.MapGet("/test-openai", async ([FromServices] ChatClient client) =>
{
    var chatMessages = new List<ChatMessage>() { new SystemChatMessage("你是一個測試助手") };
    var complete = await client.CompleteChatAsync(chatMessages);
    var resp = complete.Value.Content[0].Text;
    return Results.Ok(new { message = resp });
});

app.Run();
