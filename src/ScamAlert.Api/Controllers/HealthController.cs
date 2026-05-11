using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ScamAlert.Api.Controllers;

public sealed class HealthController : BaseApiController
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "ok",
            service = "ScamAlert.Api",
            utc = DateTimeOffset.UtcNow
        });
    }
}
