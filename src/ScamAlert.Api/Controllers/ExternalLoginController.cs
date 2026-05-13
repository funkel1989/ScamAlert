using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ScamAlert.Api.Controllers;

[AllowAnonymous]
[Route("external")]
public sealed class ExternalLoginController : ControllerBase
{
    [HttpGet("microsoft")]
    public IActionResult Microsoft(string? returnUrl = null)
    {
        var props = new AuthenticationProperties { RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/dashboard" : returnUrl };
        return Challenge(props, MicrosoftAccountDefaults.AuthenticationScheme);
    }
}
