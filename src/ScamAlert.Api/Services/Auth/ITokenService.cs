namespace ScamAlert.Api.Services.Auth;

public interface ITokenService
{
    string CreateAccessToken(
        string username,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> customerScope);
}
