using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ComputerVision.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DetectController : ControllerBase
    {
        private readonly ObjectDetectorService _detector;

        public DetectController(ObjectDetectorService detector)
        {
            _detector = detector;
        }

        public class DetectRequest
        {
            public string Image { get; set; } = "";
        }

        [HttpPost]
        public IActionResult Post([FromBody] DetectRequest req)
        {
            try
            {
                // 移除 base64 prefix
                var base64Data = req.Image.Substring(req.Image.IndexOf(",") + 1);
                var bytes = Convert.FromBase64String(base64Data);

                var results = _detector.Predict(bytes);

                return Ok(results);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }

}