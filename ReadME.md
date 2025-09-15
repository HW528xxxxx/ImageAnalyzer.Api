功能概述
影像分析 (Image Analysis)：使用 Azure Computer Vision 提取 Caption、Tags、Objects。
文字辨識 (OCR)：提取圖片中的文字。
智慧描述生成 (Azure OpenAI 2.x)：結合影像分析結果，生成更精準的描述與額外標籤。
CORS 支援：允許前端（Vite / Vue CLI）呼叫 API。
上傳限制：支援最大 20MB 的圖片檔案。

程式碼核心流程 

1. CORS 設定 : 允許前端（Vite / Vue CLI）呼叫 API。
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("frontend", p => p
        .WithOrigins("http://localhost:5173", "http://localhost:8080")
        .AllowAnyHeader()
        .AllowAnyMethod());
});


2. 上傳檔案大小限制 : 支援最大 20MB 的檔案。
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20_000_000);


3. Azure Computer Vision : 將 Azure Key 與 Endpoint 註冊到 DI。
builder.Services.AddSingleton(new ComputerVisionClient(
    new ApiKeyServiceClientCredentials(key)) { Endpoint = endpoint });


4. Swagger : 方便瀏覽 API，僅作「瀏覽」用途

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


5. Minimal API Endpoint /api/analyze

手動讀表單：
var form = await context.Request.ReadFormAsync();
var file = form.Files["file"];


1) Image Analysis (影像分析)
var features = new List<VisualFeatureTypes?>()
    { VisualFeatureTypes.Description, VisualFeatureTypes.Tags, VisualFeatureTypes.Objects };

ImageAnalysis analysis;
using (var ms1 = new MemoryStream(bytes))
    analysis = await cv.AnalyzeImageInStreamAsync(ms1, features);


呼叫 Azure Computer Vision 的 Image Analysis，要求回傳：
Description（自動生成的 caption / 描述）
Tags（標籤）
Objects（偵測到的物件）
回傳會是 ImageAnalysis 物件，裡面含 tags、objects、description 等資料。


2) OCR（Read API）
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


呼叫 ReadInStreamAsync 發起 OCR 工作（非同步長時間作業），接著從 OperationLocation 取出 operation id。
透過 GetReadResultAsync(Guid opId) 以輪詢（polling）方式檢查狀態（每 500ms 檢查一次），直到完成（Succeeded）或失敗。  --> 避免無限等待或過頻查詢
成功後可從 readResult.AnalyzeResult.ReadResults 取出每頁（page）及每行（line）的文字。


6. 呼叫 Azure OpenAI 2.x (生成更精準描述)

    // 把圖片轉成 Base64 data URI
    <!-- GPT 的 **Chat API（2.x SDK）**多數是基於訊息（message）傳送文字的。
    如果要傳圖片，API 要求 多模態訊息 (Multimodal Message) 必須用 URL 或 Data URI 的形式。
    直接傳 byte[] 是不被支援的，因為 GPT 並沒有原生接收 raw bytes 的欄位

    data:image/jpeg;base64,... 是 Data URI，本質上就是把檔案內容用 Base64 編碼後直接嵌入字串裡。
    GPT 收到後可以「理解」這是一張圖片，進行分析。
    好處：不需要把圖片上傳到雲端或生成 URL，整個訊息就包含文字和圖片-->
    string base64Image = Convert.ToBase64String(bytes);
    string imageDataUri = $"data:image/jpeg;base64,{base64Image}";


    // GPT 2.x SDK 的「多模態訊息 (文字 + 圖片)」用法
    var chatMessages = new List<ChatMessage>()
    {
        new SystemChatMessage("你是一個影像辨識專家"),
        new UserChatMessage(new List<ChatMessageContentPart>()
        {
            ChatMessageContentPart.CreateTextPart(prompt),
            ChatMessageContentPart.CreateImagePart(new Uri(imageDataUri))
        })
    };

var completion = await client.CompleteChatAsync(chatMessages);
var gptResult = completion.Value.Content[0].Text;


prompt 中包含 Caption、Tags、OCR 結果

回傳 JSON 格式：

{
    "description": "...",
    "extraTags": ["..."]
}

7. .AllowAnonymous()
確保 Minimal API 不觸發 Anti-Forgery。



8. 回傳 JSON 結果：
return Results.Ok(new
{
    tags = analysis.Tags?.Select(t => new { t.Name, t.Confidence }),
    objects = analysis.Objects?.Select(o => new { Name = o.ObjectProperty, o.Confidence }),
    caption = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault()?.Text,
    captionConfidence = analysis.Description?.Captions?.OrderByDescending(c => c.Confidence).FirstOrDefault()?.Confidence,
    ocr = ocrLines
});


C:\Users\User\Desktop\FaceAPI\ImageAnalyzer.Api\tsla.jpg
Postman 測試結果
{
    "tags": [
        {
            "name": "wheel",
            "confidence": 0.9963893890380859
        },
        {
            "name": "vehicle",
            "confidence": 0.9937546253204346
        },
        {
            "name": "tire",
            "confidence": 0.9897933006286621
        },
        {
            "name": "car",
            "confidence": 0.9815793037414551
        },
        {
            "name": "land vehicle",
            "confidence": 0.9802191257476807
        },
        {
            "name": "outdoor",
            "confidence": 0.9764155149459839
        },
        {
            "name": "ground",
            "confidence": 0.9728460311889648
        },
        {
            "name": "transport",
            "confidence": 0.9696645736694336
        },
        {
            "name": "auto part",
            "confidence": 0.954673171043396
        },
        {
            "name": "bumper",
            "confidence": 0.9361152648925781
        },
        {
            "name": "automotive design",
            "confidence": 0.9340722560882568
        },
        {
            "name": "automotive tire",
            "confidence": 0.9100607633590698
        },
        {
            "name": "automotive exterior",
            "confidence": 0.9021803140640259
        },
        {
            "name": "road",
            "confidence": 0.8984114527702332
        },
        {
            "name": "fender",
            "confidence": 0.872944176197052
        },
        {
            "name": "automotive wheel system",
            "confidence": 0.8665404319763184
        },
        {
            "name": "land rover",
            "confidence": 0.8510960340499878
        },
        {
            "name": "sky",
            "confidence": 0.8464308977127075
        },
        {
            "name": "desert",
            "confidence": 0.702564537525177
        },
        {
            "name": "silver",
            "confidence": 0.6464672684669495
        },
        {
            "name": "automotive",
            "confidence": 0.4428829550743103
        }
    ],
    "objects": [
        {
            "name": "Tire",
            "confidence": 0.514
        },
        {
            "name": "car",
            "confidence": 0.915
        }
    ],
    "caption": "a car driving on a road",
    "captionConfidence": 0.5526080131530762,
    "ocr": [],
    "gptDescription": {
        "description": "一輛特斯拉的Cybertruck電動車在沙漠中的公路上行駛。",
        "extraTags": [
            "特斯拉",
            "Cybertruck",
            "電動車",
            "沙漠"
        ]
    },
    "requestDurationMs": 7.059
}

說明
tags → Azure Computer Vision 自動識別物件與概念，例如「text」「poster」「graphic design」。

objects → 圖片中具體物件（本例中無）。

caption → 系統給的最佳文字描述。

captionConfidence → 描述的可信度。

ocr → OCR 識別出的所有文字，保持原本順序。


