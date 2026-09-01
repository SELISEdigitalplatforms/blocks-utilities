using Payment.DomainService.Entities;
using Payment.DomainService.Enums;

namespace Payment.DomainService.Repositories;

public interface IPaymentRepository
{
    Task EnsureIndexesAsync(string tenantId, CancellationToken cancellationToken);
    /// <summary>
    /// The configuration this organization pays through, falling back to the tenant's own.
    /// </summary>
    /// <remarks>
    /// Organizations within a tenant may be separate businesses with their own merchant
    /// accounts, so each may hold its own configuration. A tenant-level configuration — one
    /// with no organization — serves any organization that has not registered its own.
    /// </remarks>
    Task<PaymentProvider?> GetProviderAsync(
        string tenantId,
        string? organizationId,
        string providerName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates a provider configuration. Returns <see langword="false"/> when one already
    /// exists for the same tenant, provider and merchant, which the unique index decides.
    /// </summary>
    Task<bool> TryCreateProviderAsync(
        PaymentProvider provider,
        CancellationToken cancellationToken);

    /// <summary>Every provider configured for a tenant, enabled or not.</summary>
    Task<IReadOnlyList<PaymentProvider>> GetProvidersAsync(
        string tenantId,
        CancellationToken cancellationToken);

    Task<PaymentProvider?> GetProviderByIdAsync(
        string tenantId,
        string providerItemId,
        CancellationToken cancellationToken);

    Task<PaymentProvider?> TryUpdateProviderConfigurationAsync(
        string tenantId,
        string providerItemId,
        long expectedVersion,
        string frontendResultUrl,
        string? countryCode,
        bool manualCapture,
        int maxRefundDays,
        string? storeId,
        bool isEnabled,
        CancellationToken cancellationToken);

    Task<PaymentProvider?> TryRotateProviderCredentialsAsync(
        string tenantId,
        string providerItemId,
        long expectedVersion,
        string providerSecretsCiphertext,
        string tenantSecuritySecretsCiphertext,
        string encryptionKeyId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes the encrypted credential blobs onto a provider. Only applies when the provider
    /// has none yet, so re-running a migration cannot overwrite live credentials.
    /// </summary>
    Task<bool> SaveProviderSecretsAsync(
        string tenantId,
        string providerItemId,
        string providerSecretsCiphertext,
        string tenantSecuritySecretsCiphertext,
        string encryptionKeyId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a provider's credential blobs onto a new encryption key.
    /// </summary>
    /// <remarks>
    /// Compare-and-set on <paramref name="expectedKeyId"/> rather than on absence, because this
    /// rewrites live credentials rather than filling in missing ones. A provider whose key has
    /// already changed — by a concurrent rotation, or by an earlier run of the same job — is
    /// left alone and reported as unchanged, which is what makes a repeated run a no-op.
    /// </remarks>
    Task<bool> ReplaceProviderSecretsAsync(
        string tenantId,
        string providerItemId,
        string expectedKeyId,
        string providerSecretsCiphertext,
        string tenantSecuritySecretsCiphertext,
        string encryptionKeyId,
        CancellationToken cancellationToken);
    Task<bool> TryCreateAsync(PaymentDetail payment, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetByIdAsync(string tenantId, string paymentId, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetByPspReferenceAsync(string tenantId, string pspReference, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken);
    Task<PaymentDetail?> GetRecurringPaymentByOrderIdAsync(
        string tenantId,
        string orderId,
        CancellationToken cancellationToken);
    Task<PaymentDetail?> TryClaimInitiationAsync(string tenantId, string paymentId, string leaseId, DateTime leaseUntilUtc, CancellationToken cancellationToken);
    Task<bool> SaveInitiationRequestAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        Payment.DomainService.Models.ProviderInitiationRequest request,
        string frontendResultUrlSnapshot,
        string returnStateNonceHash,
        string shopperReference,
        CancellationToken cancellationToken,
        string? resolvedProviderId = null,
        string? resolvedProviderOrganizationId = null);
    Task<bool> CompleteInitiationAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string status,
        string? sessionId,
        string? sessionData,
        string? redirectUrl,
        DateTime? expiresAtUtc,
        string? failureCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
    Task MarkInitiationUnknownAsync(string tenantId, string paymentId, string leaseId, string failureCode, CancellationToken cancellationToken);
    Task<bool> CompleteStoredPaymentChargeInitiationAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string pspReference,
        string? providerResultCode,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
    Task<bool> SaveProviderRoutingAsync(
        string tenantId,
        string paymentId,
        string leaseId,
        string providerReference,
        string merchantAccount,
        CancellationToken cancellationToken);
    Task<bool> SaveCheckoutObservationAsync(
        string tenantId,
        string paymentId,
        string sessionStatus,
        string? resultCode,
        string sessionResultHash,
        string? pspReference,
        PaymentInstrument? instrument,
        CancellationToken cancellationToken);
    Task<bool> ApplyAuthorisationAsync(
        string tenantId,
        string paymentId,
        bool authorized,
        decimal authorizedAmount,
        bool capturedAutomatically,
        string pspReference,
        DateTime eventDateUtc,
        PaymentInstrument? instrument,
        PaymentOutboxEvent outboxEvent,
        CancellationToken cancellationToken);
    Task<List<PaymentDetail>> GetPaymentsWithDueOutboxEventsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task<bool> TryClaimOutboxEventAsync(string tenantId, string paymentId, string eventId, string leaseId, DateTime leaseUntilUtc, CancellationToken cancellationToken);
    Task MarkOutboxPublishedAsync(string tenantId, string paymentId, string eventId, string leaseId, DateTime utcNow, CancellationToken cancellationToken);
    Task MarkOutboxFailedAsync(
        string tenantId,
        string paymentId,
        string eventId,
        string leaseId,
        PaymentOutboxStatus status,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        string error,
        CancellationToken cancellationToken);
    Task<List<PaymentDetail>> GetStaleInitiationsAsync(string tenantId, DateTime utcNow, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently records that a card setup's authorization succeeded -- one of the two
    /// independent signals <see cref="Services.PaymentMethodSetupWebhookStateTransitionService"/>
    /// requires before completing a setup. First write wins: a duplicate delivery of the same
    /// webhook (or a race between two) after the field is already set is a no-op, returning
    /// <see langword="false"/> rather than overwriting an earlier timestamp.
    /// </summary>
    /// <param name="pspReference">
    /// Recorded alongside the signal so a later completion -- possibly triggered by the token
    /// arriving afterwards, not by this call -- still has the provider's own reference for the
    /// authorization to persist.
    /// </param>
    Task<bool> TryRecordSetupAuthorizationConfirmedAsync(
        string tenantId,
        string paymentId,
        DateTime eventDateUtc,
        string pspReference,
        CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently records that a card setup's recurring token was received -- the other of the
    /// two independent signals. See <see cref="TryRecordSetupAuthorizationConfirmedAsync"/>.
    /// </summary>
    Task<bool> TryRecordSetupTokenConfirmedAsync(
        string tenantId,
        string paymentId,
        DateTime eventDateUtc,
        CancellationToken cancellationToken);
    Task<bool> HasUnresolvedRecurringPaymentAsync(
        string tenantId,
        string storedPaymentMethodId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Card setups still <see cref="Enums.PaymentStatuses.Processing"/> with at least one of the
    /// two completion signals (see <see cref="TryRecordSetupAuthorizationConfirmedAsync"/> and
    /// <see cref="TryRecordSetupTokenConfirmedAsync"/>) still missing after
    /// <paramref name="olderThanUtc"/> -- Finding 3's terminal recovery path for a setup one of
    /// Adyen's two webhooks never delivered for at all.
    /// </summary>
    Task<List<PaymentDetail>> GetDueSetupExpiryCandidatesAsync(
        string tenantId,
        DateTime olderThanUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently expires a card setup that has waited past its timeout with a signal still
    /// missing, so an operator or a fresh checkout attempt is not stuck behind a setup that will
    /// never complete on its own. Compare-and-set on the status still being
    /// <see cref="Enums.PaymentStatuses.Processing"/> <em>and</em> a signal still being missing,
    /// both re-checked atomically in the same write, so a completion or decline that lands
    /// concurrently with the sweep -- even one whose signal was recorded after the candidate was
    /// read but whose completion has not finished or been retried yet -- always wins over the
    /// expiry. See PR #393 review (Finding 1): checking only the status was not sufficient, since
    /// the status stays Processing for a real window after the final signal lands and before
    /// completion runs.
    /// </summary>
    Task<bool> TryExpireSetupAsync(
        string tenantId,
        string paymentId,
        DateTime eventDateUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Card setups still <see cref="Enums.PaymentStatuses.Processing"/> that already have
    /// <em>both</em> completion signals on record -- the residual case where a process crashed
    /// between recording the final signal and calling
    /// <see cref="Services.PaymentMethodSetupCompletion.TryCompleteAsync"/>, with no further
    /// webhook redelivery left to retry it.
    /// </summary>
    /// <remarks>
    /// See PR #393 review (Finding, round 5): this used to share <c>GetPendingSetupsAsync</c>'s
    /// single "oldest N Processing setups" query with the pending-age telemetry below. Sharing it
    /// meant a setup that is fully ready to complete right now could be starved indefinitely by an
    /// unrelated backlog of older setups still genuinely missing a signal -- once that backlog
    /// exceeded the batch cap, the ready setup fell outside the oldest-first window and never got
    /// picked up until the backlog ahead of it drained. This query is filtered specifically to
    /// "both signals present", not "oldest N regardless of readiness", so a ready setup is found
    /// however large the unrelated backlog is. The caller should keep paging (this is intentionally
    /// not capped the way an expiry-candidate sweep is) until a page comes back smaller than
    /// <paramref name="limit"/> or nothing in a page actually completes.
    /// </remarks>
    Task<List<PaymentDetail>> GetSetupsReadyForCompletionAsync(
        string tenantId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Per missing-signal category ("authorization", "token", or "both"), the count of currently
    /// pending card setups still missing that signal and the oldest of their <c>CreatedAtUtc</c>
    /// timestamps -- computed by a MongoDB aggregation over every matching document for the
    /// tenant, not a capped batch read into application code.
    /// </summary>
    /// <remarks>
    /// See PR #393 review (Finding, round 5): the previous <c>payment.setup.pending_age</c>
    /// telemetry iterated whatever fit in the same capped, oldest-first batch used for the
    /// completion-recovery sweep above, so it never actually observed "every currently pending
    /// setup" as documented once a tenant had more than one batch's worth outstanding. This
    /// answers the "how old is the oldest offender in each category" question directly from Mongo
    /// instead.
    /// </remarks>
    Task<IReadOnlyList<PendingSetupAgeSummary>> GetPendingSetupAgeSummaryAsync(
        string tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// One missing-signal category's share of a tenant's currently pending card setups, as computed by
/// <see cref="IPaymentRepository.GetPendingSetupAgeSummaryAsync"/>.
/// </summary>
/// <param name="MissingSignal">"authorization", "token", or "both" -- never "none": a setup with
/// both signals present is ready to complete, not pending, and is out of scope for this summary.
/// </param>
/// <param name="Count">How many pending setups in this tenant are missing exactly this signal.</param>
/// <param name="OldestCreatedAtUtc">The earliest <c>CreatedAtUtc</c> among them -- the one an
/// operator most needs to know the age of.</param>
public sealed record PendingSetupAgeSummary(
    string MissingSignal,
    long Count,
    DateTime OldestCreatedAtUtc);
