using Microsoft.AspNetCore.Mvc;

namespace ResumableChat.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Check()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow,
            service = "ResumableChat.Api"
        });
    }
}
