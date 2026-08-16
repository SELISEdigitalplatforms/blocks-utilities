using Microsoft.Extensions.Logging;

namespace Payment.DomainService.Utilities;

/// <summary>
/// The names a payment operation goes by in the logs.
/// </summary>
/// <remarks>
/// Constants rather than literals so the set is enumerable from one place. Reconstructing what
/// happened to a payment means filtering on these, and a name that exists only as a string in
/// one file is a step of the lifecycle nobody knows to look for.
/// </remarks>
public static class PaymentOperations
{
    public const string Reserve = "payment.reserve";
    public const string Initiate = "payment.initiate";
    public const string CheckoutReturn = "payment.checkout_return";
    public const string WebhookIntake = "webhook.intake";
    public const string WebhookProcess = "webhook.process";
    public const string StateTransition = "payment.state_transition";
    public const string OutboxPublish = "outbox.publish";
    public const string RefundOutboxPublish = "outbox.publish_refund";
    public const string RefundInitiate = "refund.initiate";
    public const string CaptureInitiate = "capture.initiate";
    public const string WorkDispatch = "work.dispatch";
    public const string WorkConsume = "work.consume";
    public const string Reconcile = "payment.reconcile";
    public const string Recovery = "payment.recovery";
    public const string ProviderRegister = "provider.register";
}

/// <summary>
/// Where an operation is in its life: it started, and later it either finished or did not.
/// </summary>
/// <remarks>
/// Every operation emits <see cref="Started"/> and then exactly one of <see cref="Completed"/>
/// or <see cref="Failed"/>. That pairing is what makes an unfinished operation visible — the
/// thing that hung leaves a <c>started</c> with no partner, which no amount of error logging
/// would have shown, because nothing errored.
/// </remarks>
public static class PaymentPhases
{
    public const string Started = "started";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>
/// Builds the log scope every payment operation shares.
/// </summary>
/// <remarks>
/// One builder so the field names cannot drift apart. Log lines are joined on exact field names;
/// a processor spelling it <c>PaymentId</c> where another writes <c>PaymentDetailId</c> gives
/// two halves of a lifecycle that no query brings back together.
/// </remarks>
public static class PaymentLogScope
{
    /// <summary>
    /// Opens a scope naming the operation, the process, and the identifiers it is acting on.
    /// The correlation id comes from <see cref="PaymentCorrelation"/> rather than the caller, so
    /// it cannot be forgotten at one call site and present at the next.
    /// </summary>
    public static IDisposable? Begin(
        ILogger logger,
        string operation,
        string? tenantId = null,
        string? paymentId = null,
        string? organizationId = null,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var state = new Dictionary<string, object?>
        {
            ["CorrelationId"] = PaymentCorrelation.Current,
            ["Operation"] = operation
        };

        if (tenantId != null)
        {
            state["TenantHash"] = PaymentLogValue.Hash(tenantId);
        }

        if (paymentId != null)
        {
            state["PaymentDetailId"] = PaymentLogValue.Id(paymentId);
        }

        if (organizationId != null)
        {
            state["OrganizationId"] = PaymentLogValue.Id(organizationId);
        }

        if (extra != null)
        {
            foreach (var (key, value) in extra)
            {
                state[key] = value;
            }
        }

        return logger.BeginScope(state);
    }
}
