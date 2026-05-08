using System.Net;
using ScamAlert.Core.CloudAlerts;

namespace ScamAlert.Core.Tests.CloudAlerts;

public sealed class CloudAlertDeliveryPolicyTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true, false)]
    [InlineData(HttpStatusCode.InternalServerError, true, false)]
    [InlineData(HttpStatusCode.BadRequest, false, true)]
    [InlineData(HttpStatusCode.NotFound, false, true)]
    [InlineData(HttpStatusCode.OK, false, false)]
    public void Classifies_status_codes(
        HttpStatusCode status,
        bool transient,
        bool permanent)
    {
        Assert.Equal(transient, CloudAlertDeliveryPolicy.IsTransient(status));
        Assert.Equal(permanent, CloudAlertDeliveryPolicy.IsPermanentFailure(status));
    }
}
