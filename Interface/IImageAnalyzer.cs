using ComputerVision.Dto;

public interface IImageAnalyzer
{
    Task<ImageAnalysisResult> AnalyzeAsync(byte[] imageBytes);
}