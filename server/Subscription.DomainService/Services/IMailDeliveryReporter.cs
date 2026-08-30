using Subscription.DomainService.Enums;
using Subscription.DomainService.Messaging;

namespace Subscription.DomainService.Services;

/// <summary>
/// Records what was handed to the mail listener.
/// </summary>
/// <remarks>
/// Every method is best-effort and none of them throws. Callers sit on the mail path, where document
/// delivery is at-most-once behind a claim: an exception escaping this would unwind a send that had
/// already happened and could put a second invoice in somebody's inbox. Reporting is worth less than
/// that guarantee, so it fails quietly and says so in the log.
/// </remarks>
public interface IMailDeliveryReporter
{
    /// <summary>
    /// Records one handover and its outcome.
    /// </summary>
    /// <param name="request">What was sent, to whom, about what, and how it went.</param>
    /// <param name="cancellationToken">
    /// Honoured for the write, but a cancelled write is still not an error here: the mail's own
    /// outcome is already decided by the time this is called.
    /// </param>
    Task RecordAsync(MailDeliveryReportRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// One report to write.
/// </summary>
/// <remarks>
/// A record rather than a long parameter list: two of these fields are optional in one direction and
/// mandatory in the other (a document has a message id and a number; a usage warning has neither),
/// and positional arguments made that easy to get wrong at the call site.
/// </remarks>
public sealed record MailDeliveryReportRequest
{
    public required string TenantId { get; init; }

    public string? OrganizationId { get; init; }

    public required MailDeliveryReportSource Source { get; init; }

    public required MailDeliveryReportOutcome Outcome { get; init; }

    public required string SubjectId { get; init; }

    public string? SubjectReference { get; init; }

    public string? MailMessageId { get; init; }

    public required string ConsumerName { get; init; }

    /// <summary>
    /// The payload as published. Null when nothing was built because a guard refused first, which
    /// is a report worth keeping — "no billing contact" is exactly what an operator is looking for.
    /// </summary>
    public SendMail? Payload { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public string? CorrelationId { get; init; }
}
