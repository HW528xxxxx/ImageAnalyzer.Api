using ComputerVision.Dto;

namespace ComputerVision.Interface
{
    public interface IVideoIndexerAnalyzer
    {
        /// <summary>
        /// 對傳入的 image bytes 執行人物辨識（會上傳至 Video Indexer, 等待分析完成，回傳人物清單）。
        /// 回傳的 PersonInfo 可以包含 name/faceId/其它欄位。
        /// </summary>
        Task<IList<PersonInfoDto>> RecognizePeopleAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
    }
}
