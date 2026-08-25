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
    private readonly IOptions<SubscriptionOptions> _options;
    private readonly ILogger<SubscriptionBillingProfileGuard> _logger;

    public SubscriptionBillingProfileGuard(
        ISubscriptionBillingProfileRepository profiles,
        IOptions<SubscriptionOptions> options,
        ILogger<SubscriptionBillingProfileGuard> logger)
    {
        _profiles = profiles;
        _options = options;
        _logger = logger;
    }

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

        return BillingProfileCompleteness.MissingFields(profile);
    }

    public async Task RememberInitiatorAsync(
        string tenantId,
        string organizationId,
        string? userId,
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

            // The profile's own contact details, recorded against the acting user. A directory of
            // per-user names is not something this module has, and inventing one from an identity
            // provider would answer about who they are now rather than who they were when they acted.
            await _profiles.RecordContactAsync(
                tenantId,
                organizationId,
                new BillingContact
                {
                    UserId = userId,
                    Name = profile.BillingContactName,
                    Email = profile.BillingContactEmail
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
