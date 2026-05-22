using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ScamAlert.Api.Services.Web;

namespace ScamAlert.Api.Controllers;

[AllowAnonymous]
[Route("external")]
public sealed class ExternalLoginController : Controller
{
    [HttpGet("microsoft")]
    public IActionResult Microsoft(string? returnUrl = null)
    {
        var safeReturnUrl = ReturnUrlSanitizer.Sanitize(returnUrl);
        var props = new AuthenticationProperties { RedirectUri = safeReturnUrl };
        return Challenge(props, MicrosoftAccountDefaults.AuthenticationScheme);
    }
}
