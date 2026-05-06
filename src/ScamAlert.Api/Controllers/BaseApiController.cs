using Microsoft.AspNetCore.Mvc;

namespace ScamAlert.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
}
