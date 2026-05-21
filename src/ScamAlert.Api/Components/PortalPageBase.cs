using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ScamAlert.Api.Services.Portal;

namespace ScamAlert.Api.Components;

public abstract class PortalPageBase : ComponentBase
{
    [Inject] protected ICustomerPortalContext PortalContext { get; set; } = null!;
    [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    protected Guid? CustomerId { get; private set; }
    protected string? PortalScopeError { get; private set; }
    protected bool PortalReady { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        CustomerId = await PortalContext.TryGetSingleCustomerIdAsync(state.User, CancellationToken.None);
        PortalScopeError = CustomerId is null
            ? "This page is for a single account. If you manage multiple organizations, contact support for help."
            : null;
        PortalReady = true;
    }
}
