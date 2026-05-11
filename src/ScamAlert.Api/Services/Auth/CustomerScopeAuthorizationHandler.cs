using Microsoft.AspNetCore.Authorization;

namespace ScamAlert.Api.Services.Auth;

public sealed class CustomerScopeRequirement : IAuthorizationRequirement;

public sealed class CustomerScopeAuthorizationHandler(ICurrentUserAccessService accessService)
    : AuthorizationHandler<CustomerScopeRequirement, Guid>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CustomerScopeRequirement requirement,
        Guid customerId)
    {
        if (accessService.CanAccessCustomer(context.User, customerId))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
