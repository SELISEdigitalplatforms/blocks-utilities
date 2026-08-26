namespace Subscription.DomainService.Enums;

/// <summary>
/// What a resend request actually did.
/// </summary>
/// <remarks>
/// Three answers rather than a success flag, because "the mail will be sent" is true in all three and
/// is not the useful part. What a caller needs to know is whether <em>this</em> request is the one that
/// will send it — a second click that joined an outstanding send has succeeded, and has not caused a
/// second email.
/// </remarks>
public enum FinancialDocumentResendStatus
{
    /// <summary>
    /// This request reopened the delivery and queued the send.
    /// </summary>
    Queued = 0,

    /// <summary>
    /// A send was already outstanding, and this request joined it rather than adding another.
    /// </summary>
    /// <remarks>
    /// What a double click gets, and what a second operator asking the same question gets. Reported as
    /// success because the thing being asked for is already going to happen; reported distinctly
    /// because nothing new was scheduled and no second email will arrive.
    /// </remarks>
    JoinedPending = 1,

    /// <summary>
    /// The delivery was reopened but the queue write failed, so the sweep will carry it.
    /// </summary>
    /// <remarks>
    /// Not a failure. Reopening is committed to the document, and the delivery sweep finds outstanding
    /// documents by their delivery state without needing a queue key — so the send is waiting for that
    /// sweep rather than for a worker picking up an item.
    /// </remarks>
    AwaitingSweep = 2
}
