using Subscription.DomainService.Entities;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

/// <summary>
/// Reads and writes the tenant's own invoicing identity, and answers who is selling.
/// </summary>
public interface ISubscriptionMerchantProfileService
{
    Task<SubscriptionOperationResult<SubscriptionMerchantProfileResponse>> GetAsync(
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<SubscriptionMerchantProfileResponse>> UpdateAsync(
        UpdateMerchantProfileRequest request,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The seller to stamp on a document, from this tenant's profile or from configuration.
    /// </summary>
    /// <remarks>
    /// Never fails and never blocks issuance. By the time a document is being composed the money has
    /// moved, and refusing to record it because nobody filled in a form would lose the record of a
    /// real payment. Enforcement belongs before the charge, where refusing costs nothing — see
    /// <see cref="MissingFieldsAsync"/>.
    /// </remarks>
    Task<FinancialDocumentMerchant> ResolveAsync(
        string tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// What the tenant's selling identity still needs before it can issue a document.
    /// </summary>
    /// <returns>Empty when a stored profile or configuration names a seller.</returns>
    Task<IReadOnlyList<string>> MissingFieldsAsync(
        string tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The provider a new subscription for this tenant should be created against -- the stored
    /// selection, or <c>STRIPE</c> for a tenant that has never saved one. Read directly rather
    /// than through <see cref="GetAsync"/>, which also evaluates readiness for every registered
    /// provider for the console's own two cards -- work a subscription creation has no use for.
    /// </summary>
    Task<string> ResolveProviderNameAsync(
        string tenantId,
        CancellationToken cancellationToken);
}
