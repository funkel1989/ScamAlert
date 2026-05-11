using System.ComponentModel.DataAnnotations;

namespace ScamAlert.Api.Contracts;

public sealed record TokenRequest(
    [Required, StringLength(200)] string Username,
    [Required, StringLength(200)] string Password);

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresUtc);
