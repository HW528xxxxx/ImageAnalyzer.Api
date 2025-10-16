using ComputerVision.Dto;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

public class ObjectDetectionService
{
    private readonly InferenceSession _session;

    private static readonly string[] CocoClassNames = new string[]
    {
        "person","bicycle","car","motorbike","aeroplane","bus","train","truck","boat",
        "traffic light","fire hydrant","stop sign","parking meter","bench","bird","cat",
        "dog","horse","sheep","cow","elephant","bear","zebra","giraffe","backpack",
        "umbrella","handbag","tie","suitcase","frisbee","skis","snowboard","sports ball",
        "kite","baseball bat","baseball glove","skateboard","surfboard","tennis racket",
        "bottle","wine glass","cup","fork","knife","spoon","bowl","banana","apple",
        "sandwich","orange","broccoli","carrot","hot dog","pizza","donut","cake","chair",
        "sofa","pottedplant","bed","diningtable","toilet","tvmonitor","laptop","mouse",
        "remote","keyboard","cell phone","microwave","oven","toaster","sink","refrigerator",
        "book","clock","vase","scissors","teddy bear","hair drier","toothbrush"
    };

    private const int ModelSize = 640;

    public ObjectDetectionService(string modelPath)
    {
        _session = new InferenceSession(modelPath);
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    public List<DetectionResult> Predict(byte[] imageBytes, float scoreThreshold = 0.5f, int topN = 5)
    {
        using var image = Image.Load<Rgb24>(imageBytes);

        // === Letterbox + Resize ===
        var ratio = Math.Min((float)ModelSize / image.Width, (float)ModelSize / image.Height);
        int newW = (int)(image.Width * ratio);
        int newH = (int)(image.Height * ratio);

        image.Mutate(ctx => ctx.Resize(newW, newH));
        image.Mutate(ctx => ctx.Pad(ModelSize, ModelSize, Color.Gray));

        // === Convert to Tensor ===
        var tensor = new DenseTensor<float>(new[] { 1, 3, ModelSize, ModelSize });
        for (int y = 0; y < ModelSize; y++)
        {
            for (int x = 0; x < ModelSize; x++)
            {
                var pixel = image[x, y];
                tensor[0, 0, y, x] = pixel.R / 255f;
                tensor[0, 1, y, x] = pixel.G / 255f;
                tensor[0, 2, y, x] = pixel.B / 255f;
            }
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", tensor)
        };

        using var results = _session.Run(inputs);
        var outputArray = results.First().AsEnumerable<float>().ToArray();

        // === Decode YOLOv8 ===
        int numClasses = 80;
        int valuesPerBox = 4 + 1 + numClasses;
        int numBoxes = outputArray.Length / valuesPerBox;

        var detections = Enumerable.Range(0, numBoxes)
            .Select(i =>
            {
                int offset = i * valuesPerBox;

                float cx = outputArray[offset];
                float cy = outputArray[offset + 1];
                float w = outputArray[offset + 2];
                float h = outputArray[offset + 3];

                // ✅ Sigmoid 正規化
                float objConf = Sigmoid(outputArray[offset + 4]);
                float[] classScores = outputArray.Skip(offset + 5).Take(numClasses)
                    .Select(Sigmoid).ToArray();

                int classId = Array.IndexOf(classScores, classScores.Max());
                float classScore = classScores[classId];
                float score = objConf * classScore;

                float x1 = cx - w / 2;
                float y1 = cy - h / 2;
                float x2 = cx + w / 2;
                float y2 = cy + h / 2;

                return new DetectionResult
                {
                    Class = CocoClassNames[classId],
                    Score = score,
                    Bbox = new float[] { x1 / ratio, y1 / ratio, x2 / ratio, y2 / ratio }
                };
            })
            .Where(d => d.Score >= scoreThreshold)
            .OrderByDescending(d => d.Score)
            .Take(topN)
            .ToList();

        return detections;
    }

    public List<DetectionResult> PredictFromBase64(string base64Image)
    {
        var commaIndex = base64Image.IndexOf(',');
        string base64Data = commaIndex >= 0 ? base64Image.Substring(commaIndex + 1) : base64Image;
        byte[] bytes = Convert.FromBase64String(base64Data);
        return Predict(bytes);
    }
}
