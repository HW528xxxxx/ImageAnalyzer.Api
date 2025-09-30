using Microsoft.AspNetCore.Mvc;
using ComputerVision.Interface;
using ComputerVision.Exceptions;
using OpenAI.Chat;

namespace ComputerVision.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AzureController : ControllerBase
    {
        private readonly IImageAnalyzer _analyzer;
        private readonly IIpRateLimitService _rateLimitService;
        private readonly ITtsService _ttsService;
        private readonly ChatClient _chatClient;

        public AzureController(
            IImageAnalyzer analyzer,
            IIpRateLimitService rateLimitService,
            ITtsService ttsService,
            ChatClient chatClient)
        {
            _analyzer = analyzer;
            _rateLimitService = rateLimitService;
            _ttsService = ttsService;
            _chatClient = chatClient;
        }

        [HttpPost("analyze")]
        public async Task<IActionResult> Analyze()
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                if (!_rateLimitService.CheckLimit(ip, 5, out var remaining))
                    return StatusCode(429, new { code = (int)MessageCodeEnum.CheckLimit, message = EnumHelper.GetEnumDescription(MessageCodeEnum.CheckLimit) });

                if (!Request.HasFormContentType)
                    return BadRequest(new { code = (int)MessageCodeEnum.ImageFormatError, message = "請使用 multipart/form-data 上傳" });

                var form = await Request.ReadFormAsync();
                var file = form.Files["file"];
                if (file == null || file.Length == 0)
                    return BadRequest(new { code = (int)MessageCodeEnum.ImageNULL, message = EnumHelper.GetEnumDescription(MessageCodeEnum.ImageNULL) });

                var allowedTypes = new[] { "image/jpeg", "image/png" };
                if (!allowedTypes.Contains(file.ContentType.ToLower()))
                    return BadRequest(new { code = (int)MessageCodeEnum.ImageFormatError, message = EnumHelper.GetEnumDescription(MessageCodeEnum.ImageFormatError) });

                byte[] bytes;
                using (var ms = new MemoryStream())
                {
                    await file.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }

                var result = await _analyzer.AnalyzeAsync(bytes);
                return Ok(result);
            }
            catch (AnalyzerException aex)
            {
                return BadRequest(new { code = (int)aex.Code, message = aex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { code = (int)MessageCodeEnum.非預期系統錯誤, message = EnumHelper.GetEnumDescription(MessageCodeEnum.非預期系統錯誤) + ": " + ex.Message });
            }
        }

        [HttpPost("tts")]
        public async Task<IActionResult> Tts()
        {
            try
            {
                var form = await Request.ReadFormAsync();
                var text = form["text"].ToString();
                if (string.IsNullOrWhiteSpace(text))
                    return BadRequest(new { code = (int)MessageCodeEnum.TtsTextEmpty, message = EnumHelper.GetEnumDescription(MessageCodeEnum.TtsTextEmpty) });

                var base64Audio = await _ttsService.TextToSpeechBase64Async(text);
                if (string.IsNullOrEmpty(base64Audio))
                    return StatusCode(500, new { code = (int)MessageCodeEnum.TtsFailed, message = EnumHelper.GetEnumDescription(MessageCodeEnum.TtsFailed) });

                return Ok(new { audioBase64 = base64Audio });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { code = (int)MessageCodeEnum.非預期系統錯誤, message = EnumHelper.GetEnumDescription(MessageCodeEnum.非預期系統錯誤) + ": " + ex.Message });
            }
        }

        [HttpGet("test-openai")]
        public async Task<IActionResult> TestOpenAI()
        {
            var chatMessages = new List<ChatMessage>() { new SystemChatMessage("你是一個測試助手") };
            var complete = await _chatClient.CompleteChatAsync(chatMessages);
            var resp = complete.Value.Content[0].Text;
            return Ok(new { message = resp });
        }
    }
}
