using OpenAI.Chat; // 2.x SDK
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.AspNetCore.Mvc;
using Azure.AI.OpenAI;
using ComputerVision.Interface;
using ComputerVision.Helpers;

var builder = WebApplication.CreateBuilder(args);
// ----------------- CORS -----------------
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("frontend", p => p
        .WithOrigins("http://localhost:5173", "http://localhost:8080", "https://orange-plant-03636b100.1.azurestaticapps.net")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// ----------------- 上傳大小限制 -----------------
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20_000_000);

// ----------------- Azure Computer Vision -----------------
var endpoint = builder.Configuration["AzureVision:Endpoint"]?? throw new Exception("AzureVision:Endpoint 未設定");
var key      = builder.Configuration["AzureVision:Key"] ?? throw new Exception("AzureVision:Key 未設定");

builder.Services.AddSingleton(new ComputerVisionClient(
    new ApiKeyServiceClientCredentials(key)) { Endpoint = endpoint });

// ----------------- Azure OpenAI (2.x) -----------------
string aoaiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"] ?? throw new Exception("AzureOpenAI:Endpoint 未設定");
string aoaiKey = builder.Configuration["AzureOpenAI:Key"] ?? throw new Exception("AzureOpenAI:Key 未設定");
var deployName = builder.Configuration["AzureOpenAI:Deployment"] ?? throw new Exception("AzureOpenAI:Key 未設定");

var chatClient = new AzureOpenAIClient(
    new Uri(aoaiEndpoint),
    new System.ClientModel.ApiKeyCredential(aoaiKey))
    .GetChatClient(deployName);

builder.Services.AddSingleton(chatClient);
builder.Services.AddSingleton<IImageAnalyzer, AzureImageAnalyzer>();


// ----------------- Azure VideoIndexer -----------------
string viAccountId = builder.Configuration["VideoIndexer:AccountId"] ?? throw new Exception("VideoIndexer:AccountId 未設定");
string viLocation = builder.Configuration["VideoIndexer:Location"] ?? throw new Exception("VideoIndexer:Location 未設定");

// VideoIndexer Analyzer 注入
builder.Services.AddSingleton<IVideoIndexerAnalyzer>(sp =>
{
    return new AzureVideoIndexerAnalyzer(viAccountId, viLocation, new HttpClient());
});

// ----------------- Swagger -----------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("frontend");

app.MapPost("/api/analyze", async (HttpRequest req, [FromServices] IImageAnalyzer analyzer) =>
{
    if (!req.HasFormContentType)
        return Results.BadRequest(new { message = "請使用 multipart/form-data 上傳" });

    var form = await req.ReadFormAsync();
    var file = form.Files["file"];
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { message = "請上傳圖片檔" });

    byte[] bytes;
    using (var ms = new MemoryStream())
    {
        await file.CopyToAsync(ms);
        bytes = ms.ToArray();
    }

    var result = await analyzer.AnalyzeAsync(bytes);
    return Results.Ok(result);
});

// ----------------- Minimal API Endpoint (Azure OpenAI 2.x Test) -----------------
app.MapGet("/test-openai", async ([FromServices] ChatClient client) =>
{
    var chatMessages = new List<ChatMessage>() { new SystemChatMessage("你是一個測試助手") };
    var complete = await client.CompleteChatAsync(chatMessages);
    var resp = complete.Value.Content[0].Text;
    return Results.Ok(new { message = resp });
});

app.Run();
