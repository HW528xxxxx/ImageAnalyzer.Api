using Microsoft.Azure.CognitiveServices.Vision.Face.Models;

namespace ComputerVision.Interface
{
    public interface IFaceService
    {
        /// <summary>
        /// 偵測圖片中所有臉部
        /// </summary>
        Task<IList<DetectedFace>> DetectFacesAsync(byte[] imageBytes);

        /// <summary>
        /// 分析第一張臉的屬性 (年齡、性別、情緒…)
        /// </summary>
        Task<FaceAttributes?> AnalyzeFaceAttributesAsync(byte[] imageBytes);
    }
}