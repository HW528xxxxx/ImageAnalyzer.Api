namespace ComputerVision.Interface
{
    public interface ITtsService
    {
        /// <summary>
        /// 將文字轉成 TTS 的 Base64 音訊
        /// </summary>
        /// <param name="text">要轉換的文字</param>
        /// <param name="voice">聲音名稱，預設 "alloy"</param>
        /// <param name="format">音訊格式，"mp3" 或 "wav"</param>
        /// <returns>Base64 編碼的音訊字串</returns>
        Task<string> TextToSpeechBase64Async(string text, string voice = "alloy", string format = "mp3");
    }
}
