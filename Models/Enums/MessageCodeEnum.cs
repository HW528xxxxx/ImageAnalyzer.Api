using System.ComponentModel;

public enum MessageCodeEnum
{
    /// <summary>
    /// 訊息處理成功
    /// </summary>
    [Description("訊息處理成功")]
    Success = 0,

    /// <summary>
    /// OCR 分析失敗
    /// </summary>
    [Description("OCR 分析失敗：Azure OCR 不支援此圖片格式 (僅支援 JPG / PNG)")]
    OcrFailed = 10001,

    /// <summary>
    /// 找不到檔案
    /// </summary>
    [Description("找不到檔案，請重新上傳")]
    ImageNULL = 10002,

    /// <summary>
    /// 圖片格式錯誤
    /// </summary>
    [Description("檔案不是有效的圖片格式，請改用 JPG / PNG")]
    ImageFormatError = 10003,

    /// <summary>
    /// 今日IP呼叫次數已達上限
    /// </summary>
    [Description("今日呼叫次數已達上限")]
    CheckLimit = 10004,

    
    /// <summary>
    /// Azure OpenAI 分析失敗
    /// </summary>
    [Description("Azure OpenAI 分析失敗，請稍後再試")]
    OpenAiFailed = 10005,

    
    /// <summary>
    /// Azure Computer Vision 分析失敗
    /// </summary>
    [Description("Azure Computer Vision 分析失敗，請稍後再試")]
    ComputerVisionFailed = 10006,

    /// <summary>
    /// 未知錯誤
    /// </summary>
    [Description("系統發生非預期情況，請連絡系統管理員")]
    非預期系統錯誤 = 500,
    
    /// <summary>
    /// 連線逾時
    /// </summary>
    [Description("連線逾時，請重新執行功能")]
    連線逾時 = 522,

}