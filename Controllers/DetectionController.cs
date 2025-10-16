using ComputerVision.Dto;
using Microsoft.AspNetCore.Mvc;

namespace ComputerVision.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DetectionController : ControllerBase
    {
        private readonly ObjectDetectionService _detector;

        public DetectionController(ObjectDetectionService detector)
        {
            _detector = detector;
        }

        [HttpPost("detect")]
        public IActionResult Detect([FromBody] ImageRequestDto request)
        {
            if (string.IsNullOrEmpty(request.Image))
                return BadRequest("Image data is empty");

            // 解析 Base64
            var base64Data = request.Image.Split(',').Last(); // 移除 data:image/jpeg;base64,
            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(base64Data);
            }
            catch
            {
                return BadRequest("Invalid Base64 string");
            }

            var results = _detector.Predict(imageBytes);

            return Ok(results);
        }
    }


}
