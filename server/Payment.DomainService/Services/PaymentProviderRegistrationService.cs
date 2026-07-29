using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Providers;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

/// <summary>
/// Registers a payment provider for the calling tenant, storing its credentials encrypted on
/// the provider document.
/// </summary>
public sealed class PaymentProviderRegistrationService : IPaymentProviderRegistrationService
{
    private const string ReturnPath = "payments/validate";

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IPaymentExecutionContextResolver _contextResolver;
    private readonly IValidator<RegisterPaymentProviderRequest> _validator;
    private readonly IPaymentProviderCatalog _providerCatalog;
    private readonly IAesGcmSecretProtector _protector;
    private readonly IPaymentRepository _repository;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentProviderRegistrationService> _logger;

    public PaymentProviderRegistrationService(
        IPaymentExecutionContextResolver contextResolver,
        IValidator<RegisterPaymentProviderRequest> validator,
        IPaymentProviderCatalog providerCatalog,
        IAesGcmSecretProtector protector,
        IPaymentRepository repository,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentProviderRegistrationService> logger)
    {
        _contextResolver = contextResolver;
        _validator = validator;
        _providerCatalog = providerCatalog;
        _protector = protector;
        _repository = repository;
        _options = options;
        _logger = logger;
    }

    public async Task<PaymentOperationResult> RegisterAsync(
        RegisterPaymentProviderRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextResolution = _contextResolver.Resolve(correlationId);
        if (!contextResolution.IsSuccess) return contextResolution.Failure!;

        var validation = await _validator.ValidateAsync(request, cancellationToken);

        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];

            return PaymentOperationResult.Failure(
                PaymentFailureKind.Validation,
                string.IsNullOrWhiteSpace(failure.ErrorCode)
                    ? "payment_provider_request_invalid"
                    : failure.ErrorCode,
                failure.ErrorMessage,
                correlationId);
        }

        // The tenant comes from the caller's context, never from the request body.
        var tenantId = contextResolution.Context!.TenantId;
        var returnUrl = BuildReturnUrl();

        if (returnUrl == null)
        {
            _logger.LogError(
                "Payment provider registration is unavailable Reason=public_base_url_not_configured");

            return PaymentOperationResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_registration_unavailable",
                "Provider registration is not configured.",
                correlationId);
        }

        if (!TryProtectSecrets(
                request,
                out var providerCiphertext,
                out var tenantCiphertext,
                out var keyId))
        {
            _logger.LogError(
                "Payment provider registration is unavailable Reason=encryption_unavailable TenantHash={TenantHash}",
                PaymentLogValue.Hash(tenantId));

            return PaymentOperationResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_registration_unavailable",
                "Provider registration is not configured.",
                correlationId);
        }

        _providerCatalog.TryGetDescriptor(request.ProviderName, out var descriptor);

        var provider = new PaymentProvider
        {
            ItemId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            ProviderName = request.ProviderName.ToUpperInvariant(),
            MerchantId = request.MerchantId,
            ApiBaseUrl = string.IsNullOrWhiteSpace(request.ApiBaseUrl)
                ? descriptor.DefaultApiBaseUrl!
                : request.ApiBaseUrl,
            ReturnUrl = returnUrl,
            FrontendResultUrl = request.FrontendResultUrl,
            CountryCode = request.CountryCode,
            ManualCapture = request.ManualCapture,
            MaxRefundDays = request.MaxRefundDays,
            StoreId = request.StoreId,
            IsEnabled = true,
            ProviderSecretsCiphertext = providerCiphertext,
            TenantSecuritySecretsCiphertext = tenantCiphertext,
            SecretsEncryptionKeyId = keyId
        };

        if (!await _repository.TryCreateProviderAsync(provider, cancellationToken))
        {
            return PaymentOperationResult.Failure(
                PaymentFailureKind.Conflict,
                "payment_provider_already_registered",
                "This provider is already registered for this merchant.",
                correlationId);
        }

        _logger.LogInformation(
            "Payment provider registered Provider={Provider} TenantHash={TenantHash} MerchantSlug={MerchantSlug}",
            PaymentLogValue.Label(provider.ProviderName),
            PaymentLogValue.Hash(tenantId),
            PaymentSlug.Create(request.MerchantId));

        // Deliberately returns identifiers only: no credential ever travels back out.
        return PaymentOperationResult.Success(
            new PaymentResponse
            {
                PaymentDetailId = provider.ItemId,
                ProviderName = provider.ProviderName,
                PaymentStatus = "REGISTERED"
            },
            correlationId);
    }

    private string? BuildReturnUrl()
    {
        var baseUrl = _options.CurrentValue.PublicBaseUrl;

        if (!SafeHttpsUrl.TryParse(baseUrl, out var uri))
        {
            return null;
        }

        return new Uri(
            new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/"),
            ReturnPath).AbsoluteUri;
    }

    private bool TryProtectSecrets(
        RegisterPaymentProviderRequest request,
        out string providerCiphertext,
        out string tenantCiphertext,
        out string keyId)
    {
        tenantCiphertext = string.Empty;

        var credentials = string.Equals(
            request.ProviderName,
            PaymentConstants.StripeProvider,
            StringComparison.OrdinalIgnoreCase)
            ? (object)new StripeCredentialSecret
            {
                SecretKey = request.ApiKey,
                WebhookSigningSecret = new RotatingPaymentSecret
                {
                    Active = request.WebhookHmacKey
                }
            }
            : new ProviderCredentialSecret
            {
                ApiKey = request.ApiKey,
                StandardWebhookHmac = new RotatingPaymentSecret
                {
                    Active = request.WebhookHmacKey
                },
                TokenWebhookHmac = new RotatingPaymentSecret
                {
                    Active = request.TokenHmacKey ?? request.WebhookHmacKey
                }
            };

        var tenantSecurity = new TenantPaymentSecuritySecret
        {
            // Supplied only when migrating an existing provider. Regenerating the shopper
            // reference key would change every derived reference and orphan stored cards.
            ReturnStateHmac = new RotatingPaymentSecret
            {
                Active = request.ReturnStateHmacKey ?? CreateKey()
            },
            ShopperReferenceHmacKey = request.ShopperReferenceHmacKey ?? CreateKey()
        };

        return _protector.TryProtect(
                   JsonSerializer.Serialize(credentials, SerializerOptions),
                   out providerCiphertext,
                   out keyId) &&
               _protector.TryProtect(
                   JsonSerializer.Serialize(tenantSecurity, SerializerOptions),
                   out tenantCiphertext,
                   out _);
    }

    private static string CreateKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
