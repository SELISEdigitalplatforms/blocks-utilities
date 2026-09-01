using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Utilities;

namespace Subscription.DomainService.Services;

public sealed class SubscriptionMerchantProfileService : ISubscriptionMerchantProfileService
{
    private readonly ISubscriptionContextResolver _context;
    private readonly ISubscriptionMerchantProfileRepository _profiles;
    private readonly IValidator<UpdateMerchantProfileRequest> _validator;
    private readonly IOptions<SubscriptionOptions> _subscriptionOptions;
    private readonly IOptions<PaymentOptions> _paymentOptions;
    private readonly ISubscriptionPaymentProviderReadinessService _readiness;
    private readonly IPaymentProviderCatalog _catalog;
    private readonly ISubscriptionAuditTrail _auditTrail;
    private readonly ILogger<SubscriptionMerchantProfileService> _logger;

    public SubscriptionMerchantProfileService(
        ISubscriptionContextResolver context,
        ISubscriptionMerchantProfileRepository profiles,
        IValidator<UpdateMerchantProfileRequest> validator,
        IOptions<SubscriptionOptions> subscriptionOptions,
        IOptions<PaymentOptions> paymentOptions,
        ISubscriptionPaymentProviderReadinessService readiness,
        IPaymentProviderCatalog catalog,
        ISubscriptionAuditTrail auditTrail,
        ILogger<SubscriptionMerchantProfileService> logger)
    {
        _context = context;
        _profiles = profiles;
        _validator = validator;
        _subscriptionOptions = subscriptionOptions;
        _paymentOptions = paymentOptions;
        _readiness = readiness;
        _catalog = catalog;
        _auditTrail = auditTrail;
        _logger = logger;
    }

    public async Task<SubscriptionOperationResult<SubscriptionMerchantProfileResponse>> GetAsync(
        string correlationId,
        CancellationToken cancellationToken)
    {
        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<SubscriptionMerchantProfileResponse>(correlationId);
        }

        // Readable by any authenticated caller in the tenant, deliberately. It is printed on every
        // invoice they receive, so withholding it from the subscriber it was already sent to would
        // protect nothing.
        var stored = await _profiles.GetAsync(context.TenantId, cancellationToken);

        return SubscriptionOperationResult<SubscriptionMerchantProfileResponse>.Success(
            await MapAsync(stored, context.TenantId, context.OrganizationId, cancellationToken),
            correlationId);
    }

    public async Task<SubscriptionOperationResult<SubscriptionMerchantProfileResponse>> UpdateAsync(
        UpdateMerchantProfileRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return SubscriptionOperationResult<SubscriptionMerchantProfileResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_merchant_profile_invalid",
                "The merchant profile is invalid.",
                correlationId,
                validation.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).ToArray()));
        }

        var resolution = await _context.ResolveAsync(correlationId, null, cancellationToken);

        if (resolution.Context is not { } context)
        {
            return resolution.ToFailure<SubscriptionMerchantProfileResponse>(correlationId);
        }

        // The console alone, using the same boundary that decides who may name an organization.
        // This is the seller named in law on every document the tenant issues; a subscriber able to
        // set it could have their own invoices issued under a company of their choosing.
        if (!PaymentOrganizationScope.RequestMayNameOrganization(
                context.OrganizationId,
                _paymentOptions.Value))
        {
            return SubscriptionOperationResult<SubscriptionMerchantProfileResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_merchant_profile_forbidden",
                "Only the platform console may set the merchant profile.",
                correlationId);
        }

        var providerName = request.PaymentProviderName.Trim().ToUpperInvariant();

        // Refused before anything is persisted: a merchant profile pointed at a provider that
        // cannot actually take money would silently steer every new subscription's first charge
        // into a failure the subscriber sees, not the console operator who chose it.
        var readiness = await _readiness.CheckAsync(
            context.TenantId, context.OrganizationId, providerName, cancellationToken);

        if (readiness != SubscriptionPaymentProviderReadiness.Ready)
        {
            return SubscriptionOperationResult<SubscriptionMerchantProfileResponse>.Failure(
                PaymentFailureKind.Validation,
                ReadinessErrorCode(readiness),
                ReadinessErrorMessage(readiness, providerName),
                correlationId);
        }

        var existing = await _profiles.GetAsync(context.TenantId, cancellationToken);
        var previousProviderName = existing?.PaymentProviderName;

        var address = ToAddress(request.Address);

        var stored = await _profiles.UpsertAsync(
            new SubscriptionMerchantProfile
            {
                TenantId = context.TenantId,
                LegalName = request.LegalName.Trim(),
                DisplayName = Trimmed(request.DisplayName),
                Address = address is not null && !address.IsEmpty() ? address : null,
                TaxRegistrationId = Trimmed(request.TaxRegistrationId),
                SupportEmail = Trimmed(request.SupportEmail)?.ToLowerInvariant(),
                PaymentInstructions = Trimmed(request.PaymentInstructions),
                LogoFileId = Trimmed(request.LogoFileId),
                PrimaryColor = NormalizedHex(request.PrimaryColor),
                AccentColor = NormalizedHex(request.AccentColor),
                PaymentProviderName = providerName,
                LastUpdatedByUserId = context.UserId
            },
            cancellationToken);

        if (!string.Equals(previousProviderName, providerName, StringComparison.OrdinalIgnoreCase))
        {
            await RecordProviderChangeAsync(
                context, previousProviderName, providerName, correlationId, cancellationToken);
        }

        return SubscriptionOperationResult<SubscriptionMerchantProfileResponse>.Success(
            await MapAsync(stored, context.TenantId, context.OrganizationId, cancellationToken),
            correlationId);
    }

    private static string ReadinessErrorCode(SubscriptionPaymentProviderReadiness readiness) =>
        readiness switch
        {
            SubscriptionPaymentProviderReadiness.Unsupported =>
                "subscription_payment_provider_unsupported",
            SubscriptionPaymentProviderReadiness.NotConfigured =>
                "subscription_payment_provider_not_configured",
            SubscriptionPaymentProviderReadiness.Disabled =>
                "subscription_payment_provider_disabled",
            SubscriptionPaymentProviderReadiness.Misconfigured =>
                "subscription_payment_provider_misconfigured",
            _ => "subscription_payment_provider_credentials_unavailable"
        };

    private static string ReadinessErrorMessage(
        SubscriptionPaymentProviderReadiness readiness, string providerName) =>
        readiness switch
        {
            SubscriptionPaymentProviderReadiness.Unsupported =>
                $"{providerName} is not a supported payment provider.",
            SubscriptionPaymentProviderReadiness.NotConfigured =>
                $"{providerName} has not been configured for this tenant. Set it up on the " +
                    "Payment Providers page first.",
            SubscriptionPaymentProviderReadiness.Disabled =>
                $"{providerName} is configured but disabled. Enable it on the Payment Providers " +
                    "page first.",
            SubscriptionPaymentProviderReadiness.Misconfigured =>
                $"{providerName}'s configuration is missing required fields. Complete it on the " +
                    "Payment Providers page first.",
            _ => $"{providerName}'s credentials could not be read. Check its configuration on " +
                "the Payment Providers page."
        };

    /// <summary>
    /// Mirrors <c>PlanCatalogueService.RecordArchiveAsync</c>'s shape: best effort, never allowed
    /// to fail the save it describes. A merchant profile that really was updated must not turn
    /// into an error the caller retries because the audit write itself failed.
    /// </summary>
    private async Task RecordProviderChangeAsync(
        SubscriptionContext context,
        string? previousProviderName,
        string newProviderName,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditTrail.RecordAsync(
                new SubscriptionAuditEvent
                {
                    TenantId = context.TenantId,
                    OrganizationId = context.OrganizationId,
                    AggregateType = "MerchantProfile",
                    AggregateId = context.TenantId,
                    OperationId = correlationId,
                    CorrelationId = correlationId,
                    Operation = "MerchantProfilePaymentProviderChange",
                    Stage = "MerchantProfile",
                    Outcome = "Changed",
                    Source = "Api",
                    ActorId = context.ActorId,
                    UserId = context.UserId,
                    FromStatus = previousProviderName ?? PaymentConstants.StripeProvider,
                    ToStatus = newProviderName,
                    OccurredAtUtc = DateTime.UtcNow
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "Merchant profile payment provider audit write failed TenantHash={TenantHash} " +
                "CorrelationId={CorrelationId}",
                PaymentLogValue.Hash(context.TenantId),
                correlationId);
        }
    }

    public async Task<string> ResolveProviderNameAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var stored = await _profiles.GetAsync(tenantId, cancellationToken);

        return stored?.PaymentProviderName?.Trim() is { Length: > 0 } value
            ? value.ToUpperInvariant()
            : PaymentConstants.StripeProvider;
    }

    public async Task<FinancialDocumentMerchant> ResolveAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var stored = await _profiles.GetAsync(tenantId, cancellationToken);

        if (stored is not null && stored.IsComplete())
        {
            return new FinancialDocumentMerchant
            {
                LegalName = stored.LegalName,
                DisplayName = stored.DisplayName,
                Address = stored.Address,
                TaxRegistrationId = stored.TaxRegistrationId,
                SupportEmail = stored.SupportEmail,
                PaymentInstructions = stored.PaymentInstructions,
                LogoFileId = stored.LogoFileId,
                PrimaryColor = stored.PrimaryColor,
                AccentColor = stored.AccentColor
            };
        }

        // Configuration, for an installation that has not filled in a profile yet. Kept rather than
        // removed so upgrading does not silently blank the seller on every document issued between
        // the deployment and somebody noticing.
        return FromConfiguration();
    }

    public async Task<IReadOnlyList<string>> MissingFieldsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (!_subscriptionOptions.Value.RequireBillingProfile)
        {
            return [];
        }

        var stored = await _profiles.GetAsync(tenantId, cancellationToken);

        if (stored is not null && stored.IsComplete())
        {
            return [];
        }

        return string.IsNullOrWhiteSpace(_subscriptionOptions.Value.Invoicing.LegalName)
            ? ["merchantLegalName"]
            : [];
    }

    private FinancialDocumentMerchant FromConfiguration()
    {
        var invoicing = _subscriptionOptions.Value.Invoicing;

        var address = new BillingAddress
        {
            Line1 = invoicing.AddressLine1,
            Line2 = invoicing.AddressLine2,
            City = invoicing.City,
            Region = invoicing.Region,
            PostalCode = invoicing.PostalCode,
            CountryCode = invoicing.CountryCode
        };

        return new FinancialDocumentMerchant
        {
            LegalName = invoicing.LegalName,
            Address = address.IsEmpty() ? null : address,
            TaxRegistrationId = invoicing.TaxRegistrationId,
            SupportEmail = invoicing.SupportEmail,
            PaymentInstructions = invoicing.PaymentInstructions
        };
    }

    private async Task<SubscriptionMerchantProfileResponse> MapAsync(
        SubscriptionMerchantProfile? profile,
        string tenantId,
        string? organizationId,
        CancellationToken cancellationToken)
    {
        // Legacy documents (and any tenant that has never saved a profile at all) read as Stripe --
        // the fallback lives here and nowhere else, so it can never be confused with an explicit
        // choice by anything that persists this value.
        var effectiveProviderName = profile?.PaymentProviderName?.Trim() is { Length: > 0 } stored
            ? stored.ToUpperInvariant()
            : PaymentConstants.StripeProvider;

        var providerStatuses = await Task.WhenAll(
            _catalog.RegisteredProviderNames.Select(async name =>
                new SubscriptionMerchantProfilePaymentProviderResponse
                {
                    Name = name,
                    Status = (await _readiness.CheckAsync(
                        tenantId, organizationId, name, cancellationToken)).ToString()
                }));

        var selectedStatus = providerStatuses
            .FirstOrDefault(entry => string.Equals(
                entry.Name, effectiveProviderName, StringComparison.OrdinalIgnoreCase))
            ?.Status
            ?? SubscriptionPaymentProviderReadiness.Unsupported.ToString();

        if (profile is not null && profile.IsComplete())
        {
            return new SubscriptionMerchantProfileResponse
            {
                LegalName = profile.LegalName,
                DisplayName = profile.DisplayName,
                Address = ToResponse(profile.Address),
                TaxRegistrationId = profile.TaxRegistrationId,
                SupportEmail = profile.SupportEmail,
                PaymentInstructions = profile.PaymentInstructions,
                LogoFileId = profile.LogoFileId,
                PrimaryColor = profile.PrimaryColor,
                AccentColor = profile.AccentColor,
                IsComplete = true,
                MissingFields = [],
                LastUpdatedDateUtc = profile.LastUpdatedDateUtc,
                PaymentProviderName = effectiveProviderName,
                PaymentProviderStatus = selectedStatus,
                PaymentProviders = providerStatuses
            };
        }

        var configured = FromConfiguration();
        var complete = !string.IsNullOrWhiteSpace(configured.LegalName);

        return new SubscriptionMerchantProfileResponse
        {
            LegalName = configured.LegalName,
            DisplayName = configured.DisplayName,
            Address = ToResponse(configured.Address),
            TaxRegistrationId = configured.TaxRegistrationId,
            SupportEmail = configured.SupportEmail,
            PaymentInstructions = configured.PaymentInstructions,
            IsComplete = complete,
            MissingFields = complete ? [] : ["legalName"],
            IsInheritedFromConfiguration = true,
            LastUpdatedDateUtc = profile?.LastUpdatedDateUtc,
            PaymentProviderName = effectiveProviderName,
            PaymentProviderStatus = selectedStatus,
            PaymentProviders = providerStatuses
        };
    }

    private static BillingAddress? ToAddress(BillingAddressRequest? request) =>
        request is null
            ? null
            : new BillingAddress
            {
                Line1 = Trimmed(request.Line1),
                Line2 = Trimmed(request.Line2),
                City = Trimmed(request.City),
                Region = Trimmed(request.Region),
                PostalCode = Trimmed(request.PostalCode),
                CountryCode = Trimmed(request.CountryCode)?.ToUpperInvariant()
            };

    private static BillingAddressResponse? ToResponse(BillingAddress? address) =>
        address is null
            ? null
            : new BillingAddressResponse
            {
                Line1 = address.Line1,
                Line2 = address.Line2,
                City = address.City,
                Region = address.Region,
                PostalCode = address.PostalCode,
                CountryCode = address.CountryCode
            };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// A validated hex color, stored the one way the template ever has to read it: a leading
    /// <c>#</c>, uppercase. The validator already refused anything that is not six hex digits with
    /// an optional <c>#</c>, so this only ever has one job -- pick the single spelling everything
    /// downstream can rely on without checking again.
    /// </summary>
    private static string? NormalizedHex(string? value)
    {
        var trimmed = Trimmed(value);

        return trimmed is null
            ? null
            : "#" + trimmed.TrimStart('#').ToUpperInvariant();
    }
}
