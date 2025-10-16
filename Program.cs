using OpenAI.Chat; // 2.x SDK
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.AspNetCore.Mvc;
using Azure.AI.OpenAI;
using ComputerVision.Interface;
using ComputerVision.Services;
using ComputerVision.Exceptions;

var builder = WebApplication.CreateBuilder(args);
// ----------------- CORS -----------------
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("frontend", p => p
        .WithOrigins("http://localhost:5173", "http://localhost:8080", "https://agreeable-wave-0be44e800.2.azurestaticapps.net")
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
var deployName = builder.Configuration["AzureOpenAI:Deployment"] ?? throw new Exception("AzureOpenAI:Deployment 未設定");

var chatClient = new AzureOpenAIClient(
    new Uri(aoaiEndpoint),
    new System.ClientModel.ApiKeyCredential(aoaiKey))
    .GetChatClient(deployName);

builder.Services.AddSingleton(chatClient);
builder.Services.AddSingleton<IImageAnalyzer, AzureImageAnalyzer>();

// -----------------Azure OpenAI TTS Service -----------------
var deployttsName = builder.Configuration["AzureOpenAITTS:Deployment"] ?? throw new Exception("AzureOpenAITTS:Deployment 未設定");
var ttsApiVersion = builder.Configuration["AzureOpenAITTS:ttsApiVersion"] ?? throw new Exception("AzureOpenAITTS:ttsApiVersion 未設定");

// 註冊 HttpClient
builder.Services.AddHttpClient();

// 注入 TTS 服務，把設定統一傳進去
builder.Services.AddSingleton<ITtsService>(sp =>
    new AzureOpenAiTtsService(
        sp.GetRequiredService<IHttpClientFactory>(),
        aoaiEndpoint,
        aoaiKey,
        deployttsName,
        ttsApiVersion
    )
);

// ----------------- YOLOv8m ONNX 即時物件辨識 API -----------------
builder.Services.AddSingleton<ObjectDetectionService>(sp =>
{
    var modelPath = Path.Combine(builder.Environment.ContentRootPath, "Models", "yolov8m.onnx");
    return new ObjectDetectionService(modelPath);
});

// ----------------- Memory Cache -----------------
builder.Services.AddMemoryCache(); // 記憶體快取
builder.Services.AddSingleton<IIpRateLimitService, IpRateLimitService>();

// ----------------- Swagger -----------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ----------------- MapControllers -----------------
builder.Services.AddControllers();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("frontend");
app.MapControllers();

app.Run();
