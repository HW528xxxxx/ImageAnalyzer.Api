public class ImageAnalysisResult
{
    public IEnumerable<(string Name, double Confidence)> Tags { get; set; } = Enumerable.Empty<(string, double)>();
    public IEnumerable<(string Name, double Confidence)> Objects { get; set; } = Enumerable.Empty<(string, double)>();
    public string? Caption { get; set; }
    public double? CaptionConfidence { get; set; }
    public IEnumerable<string> OcrLines { get; set; } = Enumerable.Empty<string>();
    public GptResult? GptDescription { get; set; }
    public double RequestDurationMs { get; set; }
}
