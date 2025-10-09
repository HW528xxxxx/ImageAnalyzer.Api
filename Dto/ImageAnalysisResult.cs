namespace ComputerVision.Dto
{
    public class ObjectInfo
    {
        public string Name { get; set; } = "";
        public double Confidence { get; set; }
    }

    public class ImageAnalysisResult
    {
        public IEnumerable<ObjectInfo> Tags { get; set; } = Enumerable.Empty<ObjectInfo>();
        public IEnumerable<ObjectInfo> Objects { get; set; } = Enumerable.Empty<ObjectInfo>();
        public string? Caption { get; set; }
        public double? CaptionConfidence { get; set; }
        public IEnumerable<string> OcrLines { get; set; } = Enumerable.Empty<string>();
        public GptResult? GptDescription { get; set; }
        public double RequestDurationMs { get; set; }
        public string? AnnotatedImageBase64 { get; set; }
    }

}
