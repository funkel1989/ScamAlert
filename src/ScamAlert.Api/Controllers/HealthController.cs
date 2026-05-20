using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScamAlert.Data;

namespace ScamAlert.Api.Controllers;

public sealed class HealthController(ScamAlertDbContext dbContext) : BaseApiController
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["api"] = "ok"
        };

        try
        {
            await dbContext.Database.CanConnectAsync(cancellationToken);
            checks["database"] = "ok";
        }
        catch (Exception ex)
        {
            checks["database"] = "unavailable";
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    status = "degraded",
                    service = "ScamAlert.Api",
                    utc = DateTimeOffset.UtcNow,
                    checks,
                    error = ex.Message
                });
        }

        return Ok(new
        {
            status = "ok",
            service = "ScamAlert.Api",
            utc = DateTimeOffset.UtcNow,
            checks
        });
    }
}
