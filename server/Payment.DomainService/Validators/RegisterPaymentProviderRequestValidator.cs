using FluentValidation;
using Payment.DomainService.Providers;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;

namespace Payment.DomainService.Validators;

/// <summary>
/// Checks the shape of a registration request. Credential <em>content</em> is validated by the
/// provider's own hydrator when the configuration is first used, which is the code that already
/// owns each provider's secret schema.
/// </summary>
public sealed class RegisterPaymentProviderRequestValidator :
    AbstractValidator<RegisterPaymentProviderRequest>
{
    public RegisterPaymentProviderRequestValidator(
        IPaymentProviderCatalog providerCatalog,
        IProviderEndpointPolicyResolver endpointPolicies,
        ICheckoutUrlPolicy checkoutUrlPolicy)
    {
        ArgumentNullException.ThrowIfNull(providerCatalog);
        ArgumentNullException.ThrowIfNull(endpointPolicies);
        ArgumentNullException.ThrowIfNull(checkoutUrlPolicy);

        RuleFor(x => x.ProviderName)
            .NotEmpty()
            .Must(providerCatalog.IsRegistered)
            .WithMessage(
                $"Supported providers: {string.Join(", ", providerCatalog.RegisteredProviderNames)}.")
            .WithErrorCode("payment_provider_not_supported");

        RuleFor(x => x.MerchantId).NotEmpty().MaximumLength(200);

        RuleFor(x => x.FrontendResultUrl)
            .NotEmpty()
            .Must(checkoutUrlPolicy.IsAllowedFrontendResultUrl)
            .WithMessage("FrontendResultUrl must be a public HTTPS address.")
            .WithErrorCode("payment_frontend_url_invalid");

        RuleFor(x => x.ApiKey).NotEmpty().MaximumLength(8_192);
        RuleFor(x => x.WebhookHmacKey).NotEmpty().MaximumLength(8_192);
        RuleFor(x => x.TokenHmacKey).MaximumLength(8_192);
        RuleFor(x => x.CountryCode).Length(2).When(x => !string.IsNullOrWhiteSpace(x.CountryCode));
        RuleFor(x => x.StoreId).MaximumLength(200);
        RuleFor(x => x.MaxRefundDays).InclusiveBetween(0, 3_650);

        // A base URL is only needed when the provider has no fixed host; when one is supplied
        // it must satisfy that provider's own endpoint policy.
        RuleFor(x => x)
            .Must(request => HasUsableApiBase(request, providerCatalog, endpointPolicies))
            .WithName(nameof(RegisterPaymentProviderRequest.ApiBaseUrl))
            .WithMessage("ApiBaseUrl is required for this provider and must be one it is reachable at.")
            .WithErrorCode("payment_provider_endpoint_invalid")
            .When(request => providerCatalog.IsRegistered(request.ProviderName));

        RuleFor(x => x.ReturnStateHmacKey)
            .Must(BeABase64KeyOfAtLeast32Bytes)
            .WithMessage("ReturnStateHmacKey must be at least 32 bytes, base64 encoded.")
            .When(x => !string.IsNullOrWhiteSpace(x.ReturnStateHmacKey));

        RuleFor(x => x.ShopperReferenceHmacKey)
            .Must(BeABase64KeyOfAtLeast32Bytes)
            .WithMessage("ShopperReferenceHmacKey must be at least 32 bytes, base64 encoded.")
            .When(x => !string.IsNullOrWhiteSpace(x.ShopperReferenceHmacKey));
    }

    private static bool HasUsableApiBase(
        RegisterPaymentProviderRequest request,
        IPaymentProviderCatalog providerCatalog,
        IProviderEndpointPolicyResolver endpointPolicies)
    {
        if (!providerCatalog.TryGetDescriptor(request.ProviderName, out var descriptor))
        {
            return false;
        }

        var apiBaseUrl = string.IsNullOrWhiteSpace(request.ApiBaseUrl)
            ? descriptor.DefaultApiBaseUrl
            : request.ApiBaseUrl;

        return !string.IsNullOrWhiteSpace(apiBaseUrl) &&
               endpointPolicies.Resolve(request.ProviderName)?.IsAllowed(apiBaseUrl) == true;
    }

    private static bool BeABase64KeyOfAtLeast32Bytes(string? value)
    {
        try
        {
            return Convert.FromBase64String(value!).Length >= 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
