using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ScamAlert.Api.Contracts;
using ScamAlert.Api.Services.Auth;
using ScamAlert.Api.Services.Validation;

namespace ScamAlert.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public sealed class AccountController(IPasswordResetService passwordResetService) : ControllerBase
{
    [HttpPost("forgot-password")]
    [EnableRateLimiting("signup")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!EmailAddressValidator.TryValidate(request.Email, out var email, out var emailError))
        {
            return BadRequest(new { error = emailError });
        }

        await passwordResetService.RequestResetAsync(email, cancellationToken);
        return Ok(new { message = "If an account exists for that email, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("signup")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!PasswordPolicy.TryValidate(request.NewPassword, out var passwordError))
        {
            return BadRequest(new { error = passwordError });
        }

        var ok = await passwordResetService.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken);
        return ok
            ? Ok(new { message = "Password updated. You can log in now." })
            : BadRequest(new { error = "Invalid or expired reset link." });
    }
}
