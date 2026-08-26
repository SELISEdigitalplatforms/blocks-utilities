using FluentValidation;
using Microsoft.Extensions.Options;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
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

    public SubscriptionMerchantProfileService(
        ISubscriptionContextResolver context,
        ISubscriptionMerchantProfileRepository profiles,
        IValidator<UpdateMerchantProfileRequest> validator,
        IOptions<SubscriptionOptions> subscriptionOptions,
        IOptions<PaymentOptions> paymentOptions)
    {
        _context = context;
        _profiles = profiles;
        _validator = validator;
        _subscriptionOptions = subscriptionOptions;
        _paymentOptions = paymentOptions;
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
            Map(stored),
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
                LastUpdatedByUserId = context.UserId
            },
            cancellationToken);

        return SubscriptionOperationResult<SubscriptionMerchantProfileResponse>.Success(
            Map(stored),
            correlationId);
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
                PaymentInstructions = stored.PaymentInstructions
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

    private SubscriptionMerchantProfileResponse Map(SubscriptionMerchantProfile? profile)
    {
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
                IsComplete = true,
                MissingFields = [],
                LastUpdatedDateUtc = profile.LastUpdatedDateUtc
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
            LastUpdatedDateUtc = profile?.LastUpdatedDateUtc
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
}
