using ComputerVision.Exceptions;
using ComputerVision.Interface;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;

namespace ComputerVision.Services
{
    public class FaceService : IFaceService
    {
        private readonly IFaceClient _faceClient;

        public FaceService(IFaceClient faceClient)
        {
            _faceClient = faceClient;
        }

        public async Task<IList<DetectedFace>> DetectFacesAsync(byte[] imageBytes)
        {
            try
            {
                using var ms = new MemoryStream(imageBytes);
                var faces = await _faceClient.Face.DetectWithStreamAsync(ms,
                    returnFaceId: false,
                    recognitionModel: RecognitionModel.Recognition03,
                    detectionModel: DetectionModel.Detection01
                );
                Console.WriteLine($"偵測到 {faces.Count} 張臉");

                return faces;
            }
            catch (APIErrorException apiEx) when (apiEx.Response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new AnalyzerException(
                    MessageCodeEnum.FaceAPIFailed,
                    "Face API 權限不足或模型不支援，請確認定價層與模型",
                    apiEx
                );
            }
            catch (Exception ex)
            {
                throw new AnalyzerException(
                    MessageCodeEnum.FaceAPIFailed,
                    EnumHelper.GetEnumDescription(MessageCodeEnum.FaceAPIFailed),
                    ex
                );
            }
        }


        public async Task<FaceAttributes?> AnalyzeFaceAttributesAsync(byte[] imageBytes)
        {
            var faces = await DetectFacesAsync(imageBytes);
            return faces.FirstOrDefault()?.FaceAttributes;
        }
    }
}