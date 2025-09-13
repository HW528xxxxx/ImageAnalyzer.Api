using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.AspNetCore.Mvc; // <--- for [FromForm]

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

// ----------------- Swagger -----------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ----------------- Build App -----------------
var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("frontend");

// ----------------- Minimal API Endpoint -----------------
// API：POST /api/analyze (multipart/form-data，欄位名 file) app.MapPost("/api/analyze", async (IFormFile file, ComputerVisionClient cv) =>

// 改成手動讀取表單，避免 Anti-Forgery 問題
app.MapPost("/api/analyze", async (HttpContext context, [FromServices] ComputerVisionClient cv) =>
{
    var req = context.Request;

    if (!req.HasFormContentType)
        return Results.BadRequest(new { message = "請使用 multipart/form-data 上傳" });

    var form = await req.ReadFormAsync();
    var file = form.Files["file"]; // Postman 裡的檔案欄位名稱

    if (file == null || file.Length == 0)
        return Results.BadRequest(new { message = "請上傳圖片檔" });

    // 將檔案裝進記憶體，避免後面要重複讀兩次（Analyze + OCR）
    byte[] bytes;
    using (var ms = new MemoryStream())
    {
        await file.CopyToAsync(ms);
        bytes = ms.ToArray();
    }

    // 1) Image Analysis
    var features = new List<VisualFeatureTypes?>()
        { VisualFeatureTypes.Description, VisualFeatureTypes.Tags, VisualFeatureTypes.Objects };

    ImageAnalysis analysis;
    using (var ms1 = new MemoryStream(bytes))
        analysis = await cv.AnalyzeImageInStreamAsync(ms1, features);

    // 2) OCR
    ReadOperationResult readResult;
    using (var ms2 = new MemoryStream(bytes))
    {
        var readOp = await cv.ReadInStreamAsync(ms2);
        var opId = readOp.OperationLocation.Split('/').Last();

        do
        {
            readResult = await cv.GetReadResultAsync(Guid.Parse(opId));
            await Task.Delay(500);
        } while (readResult.Status == OperationStatusCodes.Running ||
                 readResult.Status == OperationStatusCodes.NotStarted);
    }

    var ocrLines = new List<string>();
    if (readResult.Status == OperationStatusCodes.Succeeded)
    {
        foreach (var page in readResult.AnalyzeResult.ReadResults)
            foreach (var line in page.Lines)
                ocrLines.Add(line.Text);
    }

    // 3) 回傳結果
    return Results.Ok(new
    {
        tags = analysis.Tags?.Select(t => new { t.Name, t.Confidence }) ?? Enumerable.Empty<object>(),
        objects = analysis.Objects?.Select(o => new { Name = o.ObjectProperty, o.Confidence }) ?? Enumerable.Empty<object>(),
        caption = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault()?.Text,
        captionConfidence = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault()?.Confidence,
        ocr = ocrLines
    });
})
.AllowAnonymous(); // <- 不需要 Anti-Forgery

// ----------------- Run -----------------
app.Run();
