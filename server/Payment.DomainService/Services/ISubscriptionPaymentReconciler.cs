namespace Payment.DomainService.Services;

/// <summary>
/// Asks the provider what actually happened to a payment whose webhook never arrived, and applies
/// the answer through the same state-transition path a real webhook would have used.
/// </summary>
/// <remarks>
/// Exists for the moment a subscription's activation sweep has exhausted its retry budget and
/// still cannot say what a payment decided: rather than treat silence as failure, this reads the
/// provider directly and, when it has a decided answer, records it exactly as
/// <see cref="IPaymentWebhookStateTransitionService"/> or
/// <see cref="IPaymentMethodSetupWebhookStateTransitionService"/> would -- so a later, genuinely
/// late webhook for the same event can never double-apply or disagree with what this already
/// recorded. See <see cref="Repositories.IPaymentRepository.ApplyAuthorisationAsync"/>'s
/// deduplication-key guard, which is what makes that safe.
/// </remarks>
public interface ISubscriptionPaymentReconciler
{
    /// <summary>
    /// Supported for a provider this reconciler cannot observe returns <see langword="false"/>,
    /// as does one it can observe but that has not yet decided (still open, or unreachable) --
    /// callers must treat both the same way: keep waiting, never treat silence as failure.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> only when the provider gave a decided answer and it was applied.
    /// </returns>
    Task<bool> TryReconcileAsync(
        string tenantId,
        string paymentId,
        CancellationToken cancellationToken);
}
