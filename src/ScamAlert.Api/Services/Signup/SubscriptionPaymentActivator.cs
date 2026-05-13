using Microsoft.EntityFrameworkCore;
using ScamAlert.Data;
using ScamAlert.Data.Enums;

namespace ScamAlert.Api.Services.Signup;

public interface ISubscriptionPaymentActivator
{
    Task<bool> TryActivateCustomerAsync(Guid customerId, CancellationToken cancellationToken);
}

public sealed class SubscriptionPaymentActivator(ScamAlertDbContext dbContext) : ISubscriptionPaymentActivator
{
    public async Task<bool> TryActivateCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await dbContext.Customers
            .Include(x => x.Subscriptions)
            .SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);

        if (customer is null)
        {
            return false;
        }

        var sub = customer.Subscriptions.OrderByDescending(x => x.StartsUtc).FirstOrDefault();
        if (sub is null)
        {
            return false;
        }

        if (sub.Status == SubscriptionStatus.Active)
        {
            return true;
        }

        if (sub.Status != SubscriptionStatus.PendingPayment)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        sub.Status = SubscriptionStatus.Active;
        sub.UpdatedUtc = now;
        customer.UpdatedUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
