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
    /// 圖片格式錯誤
    /// </summary>
    [Description("檔案不是有效的圖片格式，請改用 JPG / PNG")]
    ImageFormatError = 10002,

    /// <summary>
    /// 伺服器內部錯誤
    /// </summary>
    [Description("伺服器發生未預期錯誤，請稍後再試")]
    ServerError = 10003,

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