namespace Subscription.DomainService.Enums;

/// <summary>
/// What became of one handover to the mail listener.
/// </summary>
/// <remarks>
/// Deliberately not "Delivered". Nothing here observes an inbox: the furthest this side can see is
/// that the broker accepted the message. Whether it was rendered, submitted to SES, accepted,
/// bounced or filtered is recorded by the mail service on the other side of the queue, and a report
/// that said "Delivered" would be claiming knowledge this process does not have.
/// </remarks>
public enum MailDeliveryReportOutcome
{
    /// <summary>The broker accepted the message. Nothing beyond that is claimed.</summary>
    Published = 0,

    /// <summary>
    /// The publish threw. The message may still have gone out — an acknowledgement can be lost on
    /// the way back — which is why the delivery service treats this as unknown rather than as a
    /// failure to retry.
    /// </summary>
    PublishFailed = 1,

    /// <summary>
    /// Never handed over: a guard refused first, e.g. a document with no billing contact, or a
    /// claim already taken by an earlier attempt.
    /// </summary>
    NotAttempted = 2
}
