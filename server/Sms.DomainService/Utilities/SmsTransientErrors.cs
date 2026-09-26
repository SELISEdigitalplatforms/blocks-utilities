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
    public static bool IsTransient(Exception exception) => exception switch
    {
        ApiException twilio => IsTransientStatus(twilio.Status),
        ApiConnectionException => true,
        TelnyxException { HttpStatusCode: not 0 } telnyx => IsTransientStatus((int)telnyx.HttpStatusCode),
        HttpRequestException or TimeoutException or TaskCanceledException => true,
        // Unknown types fail fast: a bug or a bad request retried for 15 minutes only hides it.
        // An SDK that wraps a network fault (Telnyx without a status code) is judged by the cause.
        _ => exception.InnerException is { } inner && IsTransient(inner)
    };

    private static bool IsTransientStatus(int status) =>
        status == (int)HttpStatusCode.TooManyRequests ||
        status == (int)HttpStatusCode.RequestTimeout ||
        status >= 500;
}
