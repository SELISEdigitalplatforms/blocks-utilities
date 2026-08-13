using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <inheritdoc />
public sealed class PaymentOrganizationResolver : IPaymentOrganizationResolver
{
    private readonly IOrganizationDirectory _organizations;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentOrganizationResolver> _logger;

    public PaymentOrganizationResolver(
        IOrganizationDirectory organizations,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentOrganizationResolver> logger)
    {
        _organizations = organizations;
        _options = options;
        _logger = logger;
    }

    public async Task<PaymentOrganizationResolution> ResolveAsync(
        string? requestedOrganizationId,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requested = requestedOrganizationId?.Trim();

        // Naming nothing is the original behaviour, and it never reaches IAM: the common
        // path keeps costing nothing and gains no new way to fail.
        if (string.IsNullOrEmpty(requested))
        {
            return new PaymentOrganizationResolution(context.OrganizationId, null);
        }

        if (!_options.CurrentValue.VerifyOrganizationWithIam)
        {
            // Logged every time, at warning, because an unverified organization is a real gap
            // and a silent gap is worse than an inconvenient one: within the tenant the caller
            // is now trusted to name any organization, including one they have no part in.
            _logger.LogWarning(
                "Accepting an unverified organization Reason=iam_verification_disabled TenantHash={TenantHash} OrganizationHash={OrganizationHash}",
                PaymentLogValue.Hash(context.TenantId),
                PaymentLogValue.Hash(requested));

            return new PaymentOrganizationResolution(requested, null);
        }

        // Naming the organization the context already carries proves nothing the token did
        // not, and the token is the stronger evidence, so there is nothing to verify.
        if (string.Equals(
                requested,
                context.OrganizationId,
                StringComparison.Ordinal))
        {
            return new PaymentOrganizationResolution(requested, null);
        }

        var outcome = await _organizations.FindAsync(requested, cancellationToken);

        return outcome switch
        {
            OrganizationLookupOutcome.Found =>
                new PaymentOrganizationResolution(requested, null),

            OrganizationLookupOutcome.NotFound =>
                new PaymentOrganizationResolution(
                    null,
                    PaymentOperationResult.Failure(
                        PaymentFailureKind.Validation,
                        "organization_not_found",
                        "The requested organization does not exist for this tenant.",
                        correlationId)),

            // Fail closed. Writing under an organization nobody could confirm is how a
            // provider ends up encrypted against a key ring serving the wrong business, and
            // how a payment ends up billed to a merchant account that is not the one the
            // shopper thought they were paying.
            _ => new PaymentOrganizationResolution(
                null,
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Unavailable,
                    "organization_verification_unavailable",
                    "The organization could not be verified. Try again.",
                    correlationId))
        };
    }
}
