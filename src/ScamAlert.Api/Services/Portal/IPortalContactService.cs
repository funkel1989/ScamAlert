using ScamAlert.Api.Contracts;

namespace ScamAlert.Api.Services.Portal;

public interface IPortalContactService
{
    Task<IReadOnlyList<PortalContactResponse>> ListAsync(Guid customerId, CancellationToken cancellationToken);

    Task<PortalContactResponse> CreateAsync(
        Guid customerId,
        CreatePortalContactRequest request,
        CancellationToken cancellationToken);

    Task<PortalContactResponse?> UpdateAsync(
        Guid customerId,
        Guid contactId,
        UpdatePortalContactRequest request,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid customerId, Guid contactId, CancellationToken cancellationToken);
}
