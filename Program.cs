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

// ----------------- Memory Cache -----------------
builder.Services.AddMemoryCache(); // 記憶體快取
builder.Services.AddSingleton<IIpRateLimitService, IpRateLimitService>();

// ----------------- Swagger -----------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("frontend");

app.MapPost("/api/analyze", async (HttpRequest req,
                                   [FromServices] IImageAnalyzer analyzer,
                                   [FromServices] IIpRateLimitService rateLimitService) =>
{
    try
    {
        var ip = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!rateLimitService.CheckLimit(ip, 5, out var remaining))
            return Results.Json(
                new { code = (int)MessageCodeEnum.CheckLimit, message = EnumHelper.GetEnumDescription(MessageCodeEnum.CheckLimit) },
                statusCode: 429
            );

        if (!req.HasFormContentType)
            return Results.Json(
                new { code = (int)MessageCodeEnum.ImageFormatError, message = "請使用 multipart/form-data 上傳" },
                statusCode: 400
            );

        var form = await req.ReadFormAsync();
        var file = form.Files["file"];
        if (file == null || file.Length == 0)
            return Results.Json(
                new { code = (int)MessageCodeEnum.ImageNULL, message = EnumHelper.GetEnumDescription(MessageCodeEnum.ImageNULL) },
                statusCode: 400
            );

        var allowedTypes = new[] { "image/jpeg", "image/png" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
            return Results.Json(
                new { code = (int)MessageCodeEnum.ImageFormatError, message = EnumHelper.GetEnumDescription(MessageCodeEnum.ImageFormatError) },
                statusCode: 400
            );

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        var result = await analyzer.AnalyzeAsync(bytes);
        return Results.Ok(result);
    }
    catch (AnalyzerException aex)
    {
        return Results.Json(
            new { code = (int)aex.Code, message = aex.Message },
            statusCode: 400
        );
    }
    catch (Exception ex)
    {
        // 其他未知錯誤
        return Results.Json(
            new { code = (int)MessageCodeEnum.非預期系統錯誤, message = EnumHelper.GetEnumDescription(MessageCodeEnum.非預期系統錯誤) + ": " + ex.Message },
            statusCode: 500
        );
    }
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
