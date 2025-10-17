using ComputerVision.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComputerVision.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotifyController : ControllerBase
    {
        private readonly LineNotifyService _lineService;

        public NotifyController(LineNotifyService lineService)
        {
            _lineService = lineService;
        }

        [HttpPost("enter")]
        public async Task<IActionResult> NotifyEnter()
        {
            try
            {
                string ipString = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                    ?? HttpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "未知 IP";

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string message = $"[{timestamp}]" + Environment.NewLine +
                 $" 訪客進入影像辨識網站 IP: 【{ipString}】";

                await _lineService.SendMessageAsync(message);
                return Ok("通知已送出");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { code = (int)MessageCodeEnum.LineNotify錯誤, message = EnumHelper.GetEnumDescription(MessageCodeEnum.LineNotify錯誤) + ": " + ex.Message });
            }            
        }
    }
}
