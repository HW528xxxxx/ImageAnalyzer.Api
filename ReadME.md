# 未來影像分析中心(後端)

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


## Image Analysis (影像分析)
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


## OCR（Read API）
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


## 呼叫 Azure OpenAI 2.x (生成更精準描述)
    ### ImageSharp 縮圖 + Base64 Data URI

    使用 [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 對上傳的圖片進行縮圖，並轉為 Base64 字串，方便在 HTTP 請求中傳送或給 GPT multimodal 使用。
    // 使用 ImageSharp 載入圖片到記憶體 (image 變數代表圖片的整個資料，包含像素資訊和寬高)
    using var image = SixLabors.ImageSharp.Image.Load(bytes);
    Image.Load(bytes) 會從 byte array 中載入圖片，不需要寫入檔案。

    // 設定縮圖的最大邊長（寬或高）為 256 像素。
    int maxSize = 256;
    
    // 根據原圖寬高比計算縮圖後的寬和高。
    int width = image.Width > image.Height ? maxSize : image.Width * maxSize / image.Height;
    int height = image.Height >= image.Width ? maxSize : image.Height * maxSize / image.Width;
    
    // 使用 Mutate 來修改原圖（ImageSharp 的不可變設計）。
    image.Mutate(x => x.Resize(width, height));
    
    Resize(width, height) 根據前面計算的尺寸縮放圖片。

    // 建立記憶體流，用來存放縮圖後的 JPEG 資料 -> 不需存檔到硬碟，提高效能。
    using var msThumb = new MemoryStream();
    

    var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
    {
        Quality = 70 // 降低 JPEG 品質，減少 payload
    };
    使用 JPEG 編碼器將圖片轉為 JPEG 格式。
    適合網路傳輸或作為 GPT multimodal 輸入。

    // 將縮圖結果寫入 MemoryStream。
    image.Save(msThumb, encoder); // 輸出 JPEG
    
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


## 呼叫 Azure OpenAI 的 TTS 
透過 HttpClient 呼叫 Azure OpenAI 的 TTS endpoint(目前僅提供REST呼叫)，把傳入的文字送到 /openai/deployments/{deployment}/audio/speech，如果回傳成功就把回傳的 audio bytes 轉成 Base64 字串並回傳

讀出整個回應的 byte 陣列（音訊檔），轉成 base64 字串回傳 -> 方便 JSON 傳遞給前端
var audioBytes = await response.Content.ReadAsByteArrayAsync();
return Convert.ToBase64String(audioBytes);


## /api/analyze 回傳 JSON 結果：
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
            "confidence": 0.9897932410240173
        },
        {
            "name": "car",
            "confidence": 0.9815791845321655
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
            "confidence": 0.9696646928787231
        },
        {
            "name": "auto part",
            "confidence": 0.9546732902526855
        },
        {
            "name": "bumper",
            "confidence": 0.9361151456832886
        },
        {
            "name": "automotive design",
            "confidence": 0.9340723156929016
        },
        {
            "name": "automotive tire",
            "confidence": 0.9100607633590698
        },
        {
            "name": "automotive exterior",
            "confidence": 0.9021801948547363
        },
        {
            "name": "road",
            "confidence": 0.8984119892120361
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
    "captionConfidence": 0.7020159338712693,
    "ocrLines": [],
    "gptDescription": {
        "description": "這是一輛特斯拉 Cybertruck，該車型以其未來感十足的設計和獨特的鋼製車身聞名。Cybertruck 擁有極高的耐用性和卓越的越野性能，外觀方正且棱角分明，車頂運用強化玻璃，增強安全性與視野。",
        "extraTags": [
            "電動車",
            "車輛",
            "越野",
            "特斯拉",
            "設計",
            "未來感",
            "頑固耐用",
            "電動交通工具",
            "科技創新"
        ]
    },
    "requestDurationMs": 6.217
}


## /api/tts 回傳 JSON 結果(轉成 base64 字串回傳前端)：
{
    "audioBase64": "//PExABbFDnIAVnAADlnOms66znfNUVYxhGGowbShvsmqiZo4GDLZgEEBBpJqqGSWZpZigoLuvACwiEgtoAjlx11z+33WHUHafLHQS8LOGAACAWgQCJEIprHa+5bW1A0x110zD1TqnVOmOmOsd+4AVOy+mfycia5y65jGbmGgSGkMsPXe48vzqQw1hdjEH4xp8pXbiC5C/gAAYAFoEwGXuWztr7vy+3qkjcbp6eNxu04CQgBCYxmMpjGWbRTZe7a70AiKixGuQ5YpHYWEQDorqDqnUDQloB0i11u47DkNcdyHKTlPDksm3DXeqdU7E4fvVIYhy9E3/"
}