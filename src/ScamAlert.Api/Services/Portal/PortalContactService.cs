using Microsoft.EntityFrameworkCore;
using ScamAlert.Api.Contracts;
using ScamAlert.Data;
using ScamAlert.Data.Entities;

namespace ScamAlert.Api.Services.Portal;

public sealed class PortalContactService(ScamAlertDbContext dbContext) : IPortalContactService
{
    public async Task<IReadOnlyList<PortalContactResponse>> ListAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return await dbContext.Contacts.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderBy(x => x.EscalationOrder)
            .Select(x => new PortalContactResponse(
                x.Id,
                x.FullName,
                x.PhoneNumber,
                x.EscalationOrder,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<PortalContactResponse> CreateAsync(
        Guid customerId,
        CreatePortalContactRequest request,
        CancellationToken cancellationToken)
    {
        ValidateContact(request.FullName, request.PhoneNumber, request.EscalationOrder);
        await EnsureEscalationOrderAvailableAsync(customerId, request.EscalationOrder, excludeContactId: null, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            EscalationOrder = request.EscalationOrder,
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        dbContext.Contacts.Add(contact);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(contact);
    }

    public async Task<PortalContactResponse?> UpdateAsync(
        Guid customerId,
        Guid contactId,
        UpdatePortalContactRequest request,
        CancellationToken cancellationToken)
    {
        ValidateContact(request.FullName, request.PhoneNumber, request.EscalationOrder);

        var contact = await dbContext.Contacts
            .SingleOrDefaultAsync(x => x.Id == contactId && x.CustomerId == customerId, cancellationToken);

        if (contact is null)
        {
            return null;
        }

        await EnsureEscalationOrderAvailableAsync(
            customerId,
            request.EscalationOrder,
            excludeContactId: contactId,
            cancellationToken);

        contact.FullName = request.FullName.Trim();
        contact.PhoneNumber = request.PhoneNumber.Trim();
        contact.EscalationOrder = request.EscalationOrder;
        contact.IsActive = request.IsActive;
        contact.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(contact);
    }

    public async Task<bool> DeleteAsync(Guid customerId, Guid contactId, CancellationToken cancellationToken)
    {
        var contact = await dbContext.Contacts
            .SingleOrDefaultAsync(x => x.Id == contactId && x.CustomerId == customerId, cancellationToken);

        if (contact is null)
        {
            return false;
        }

        if (contact.IsActive)
        {
            var activeCount = await dbContext.Contacts.CountAsync(
                x => x.CustomerId == customerId && x.IsActive,
                cancellationToken);

            if (activeCount <= 1)
            {
                throw new InvalidOperationException("Cannot delete the only active trusted contact.");
            }
        }

        dbContext.Contacts.Remove(contact);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void ValidateContact(string fullName, string phoneNumber, int escalationOrder)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > 200)
        {
            throw new ArgumentException("Contact name is required (max 200 characters).");
        }

        if (string.IsNullOrWhiteSpace(phoneNumber) || phoneNumber.Trim().Length > 32)
        {
            throw new ArgumentException("Phone number is required (E.164, max 32 characters).");
        }

        if (escalationOrder < 1 || escalationOrder > 99)
        {
            throw new ArgumentException("Escalation order must be between 1 and 99.");
        }
    }

    private async Task EnsureEscalationOrderAvailableAsync(
        Guid customerId,
        int escalationOrder,
        Guid? excludeContactId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Contacts.Where(x => x.CustomerId == customerId && x.EscalationOrder == escalationOrder);
        if (excludeContactId.HasValue)
        {
            query = query.Where(x => x.Id != excludeContactId.Value);
        }

        if (await query.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Escalation order {escalationOrder} is already used by another contact.");
        }
    }

    private static PortalContactResponse Map(Contact x) =>
        new(x.Id, x.FullName, x.PhoneNumber, x.EscalationOrder, x.IsActive);
}
