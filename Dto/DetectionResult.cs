namespace ComputerVision.Dto
{
    public class DetectionResult
    {
        public string Class { get; set; } = "";
        public float Score { get; set; }
        public float[] Bbox { get; set; } = new float[4];
    }
}