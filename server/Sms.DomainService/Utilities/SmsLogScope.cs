using Microsoft.Extensions.Logging;

namespace Sms.DomainService.Utilities;

/// <summary>
/// The log scope every unit of SMS work runs in, so each line it writes can be traced to its tenant,
/// its correlation id and its message, as the subscription and payment dispatchers do.
/// </summary>
/// <remarks>
/// The platform's log store keeps only the rendered message plus TenantId, TraceId and SpanId.
/// <c>SearchableIdLogSink</c> appends <see cref="MessageId"/> and <c>CorrelationId</c> from this scope
/// to the message text so they survive; TenantId is kept by the store itself. Values are sanitized:
/// several arrive from outside (request body, webhook route, broker command).
/// </remarks>
public static class SmsLogScope
{
    /// <summary>Must match the name <c>SearchableIdLogSink</c> appends.</summary>
    public const string MessageId = "SmsMessageId";

    public static IDisposable? Begin(ILogger logger, string? tenantId, string? correlationId, string? messageId = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var state = new Dictionary<string, object?>
        {
            ["TenantId"] = SmsLogSanitizer.Id(tenantId),
            ["CorrelationId"] = SmsLogSanitizer.Id(correlationId)
        };

        // Only a real id: a scope value overrides an outer one of the same name, so "missing" here
        // would hide the message id an enclosing scope already carries.
        if (!string.IsNullOrWhiteSpace(messageId))
        {
            state[MessageId] = SmsLogSanitizer.Id(messageId);
        }

        return logger.BeginScope(state);
    }
}
