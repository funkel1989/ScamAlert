using System.Net;

namespace ScamAlert.Core.CloudAlerts;

public static class CloudAlertDeliveryPolicy
{
    public static bool IsTransient(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || code >= 500;
    }

    /// <summary>HTTP failures that should not be retried (move to dead-letter).</summary>
    public static bool IsPermanentFailure(HttpStatusCode statusCode)
    {
        if (IsTransient(statusCode))
        {
            return false;
        }

        var code = (int)statusCode;
        return code >= 400;
    }
}
