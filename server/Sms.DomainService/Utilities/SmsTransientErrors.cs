using System.Net;
using Telnyx;
using Twilio.Exceptions;

namespace Sms.DomainService.Utilities;

/// <summary>
/// Decides whether a provider failure is worth waiting for. True sends the recipient back to the
/// retry queue with backoff; false fails that recipient for good.
/// </summary>
public static class SmsTransientErrors
{
    public static bool IsTransient(Exception exception)
    {
        // TODO(human)
        return false;
    }

    private static bool IsTransientStatus(int status) =>
        status == (int)HttpStatusCode.TooManyRequests ||
        status == (int)HttpStatusCode.RequestTimeout ||
        status >= 500;
}
