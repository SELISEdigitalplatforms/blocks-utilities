using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionBillingProfileGuard : ISubscriptionBillingProfileGuard
{
    private readonly ISubscriptionBillingProfileRepository _profiles;
    private readonly ISubscriptionMerchantProfileService _merchants;
    private readonly IOptions<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionBillingProfileGuard> _logger;

    public SubscriptionBillingProfileGuard(
        ISubscriptionBillingProfileRepository profiles,
        ISubscriptionMerchantProfileService merchants,
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionBillingProfileGuard> logger)
    {
        _profiles = profiles;
        _merchants = merchants;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// What is still missing before a document can be issued: buyer and seller both.
    /// </summary>
    /// <remarks>
    /// Both sides in one answer, because both are required for the same reason and refusing over one
    /// while ignoring the other would let a charge through that still cannot produce a valid invoice.
    /// The subscriber's fields are the ones a subscriber can fix; the seller's are the tenant's own
    /// configuration, and reporting them here is what stops a deployment issuing documents that name
    /// nobody as the seller.
    /// </remarks>
    public async Task<IReadOnlyList<string>> MissingFieldsAsync(
        string tenantId,
        string organizationId,
        CancellationToken cancellationToken)
    {
        if (!_options.Value.RequireBillingProfile)
        {
            return [];
        }

        var profile = await _profiles.GetAsync(tenantId, organizationId, cancellationToken);

        return
        [
            .. BillingProfileCompleteness.MissingFields(profile),
            .. await _merchants.MissingFieldsAsync(tenantId, cancellationToken)
        ];
    }

    public async Task RememberInitiatorAsync(
        string tenantId,
        string organizationId,
        string? userId,
        string? name,
        string? email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        try
        {
            var profile = await _profiles.GetAsync(tenantId, organizationId, cancellationToken);

            if (profile is null)
            {
                return;
            }

            // The acting person's own name and address, taken from the authenticated context. This
            // used to copy the organization's billing contact, which made every document say the
            // finance mailbox had changed the plan however many different employees actually did — the
            // one thing the field exists to record.
            //
            // Recorded when they act rather than looked up when the document is written, so a rename
            // or a departure afterwards cannot change what an issued document says about who acted.
            await _profiles.RecordContactAsync(
                tenantId,
                organizationId,
                new BillingContact
                {
                    UserId = userId,
                    Name = name is { Length: > 0 } present ? present : userId,
                    Email = email
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "A billing contact could not be recorded for the initiating user " +
                "OrganizationHash={OrganizationHash}",
                PaymentLogValue.Hash(organizationId));
        }
    }
}
