using ComputerVision.Dto;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public class ObjectDetectorService
{
    private readonly InferenceSession _session;

    public ObjectDetectorService(string modelPath)
    {
        _session = new InferenceSession(modelPath);
    }

    public List<DetectionResult> Predict(byte[] imageBytes)
    {
        using var original = Image.Load<Rgb24>(imageBytes);

        // === Letterbox (保持比例 + 填灰邊) ===
        int modelSize = 640;
        var ratio = Math.Min((float)modelSize / original.Width, (float)modelSize / original.Height);
        int newW = (int)(original.Width * ratio);
        int newH = (int)(original.Height * ratio);

        using var resized = original.Clone(ctx =>
        {
            ctx.Resize(newW, newH);
            ctx.BackgroundColor(Color.Gray);
            ctx.Pad(modelSize, modelSize);
        });

        // === 轉 Tensor ===
        var tensor = new DenseTensor<float>(new[] { 1, 3, modelSize, modelSize });
        for (int y = 0; y < modelSize; y++)
        {
            for (int x = 0; x < modelSize; x++)
            {
                var pixel = resized[x, y];
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
        var output = results.First().AsEnumerable<float>().ToArray();

        int numClasses = 80;
        int valuesPerBox = 4 + 1 + numClasses;
        int numBoxes = output.Length / valuesPerBox;

        var detections = new List<DetectionResult>();
        for (int i = 0; i < numBoxes; i++)
        {
            int offset = i * valuesPerBox;

            float cx = output[offset];
            float cy = output[offset + 1];
            float w  = output[offset + 2];
            float h  = output[offset + 3];
            float objConf = output[offset + 4];

            float[] classScores = output.Skip(offset + 5).Take(numClasses).ToArray();
            int classId = Array.IndexOf(classScores, classScores.Max());
            float classScore = classScores[classId];
            float score = objConf * classScore;

            if (score < 0.5f) continue;

            float x1 = (cx - w / 2 - (modelSize - newW) / 2f) / ratio;
            float y1 = (cy - h / 2 - (modelSize - newH) / 2f) / ratio;
            float x2 = (cx + w / 2 - (modelSize - newW) / 2f) / ratio;
            float y2 = (cy + h / 2 - (modelSize - newH) / 2f) / ratio;

            detections.Add(new DetectionResult
            {
                Class = CocoClassNames[classId],
                Score = score,
                Bbox = new float[] { x1, y1, x2, y2 }
            });
        }

        // === NMS 過濾重疊框 ===
        ApplyNms(detections, 0.45f);

        var best = detections.OrderByDescending(d => d.Score).FirstOrDefault();

        return best != null ? new List<DetectionResult> { best } : new List<DetectionResult>();
    }

    private static List<DetectionResult> ApplyNms(List<DetectionResult> detections, float iouThreshold)
    {
        var results = new List<DetectionResult>();

        foreach (var group in detections.GroupBy(d => d.Class))
        {
            var boxes = group.OrderByDescending(x => x.Score).ToList();

            while (boxes.Count > 0)
            {
                var best = boxes[0];
                results.Add(best);
                boxes.RemoveAt(0);

                boxes = boxes.Where(b => IoU(best.Bbox, b.Bbox) < iouThreshold).ToList();
            }
        }

        return results;
    }

    private static float IoU(float[] boxA, float[] boxB)
    {
        float xA = Math.Max(boxA[0], boxB[0]);
        float yA = Math.Max(boxA[1], boxB[1]);
        float xB = Math.Min(boxA[2], boxB[2]);
        float yB = Math.Min(boxA[3], boxB[3]);
        float interArea = Math.Max(0, xB - xA) * Math.Max(0, yB - yA);

        float boxAArea = (boxA[2] - boxA[0]) * (boxA[3] - boxA[1]);
        float boxBArea = (boxB[2] - boxB[0]) * (boxB[3] - boxB[1]);
        return interArea / (boxAArea + boxBArea - interArea);
    }

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
}
