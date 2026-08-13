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
    private readonly IOrganizationDirectory _organizations;
    private readonly IProviderTokenEncryptionKeyRingProvider _keyRings;
    private readonly IPaymentKeyRingStore _keyRingStore;
    private readonly IPaymentDistributedLock _locks;
    private readonly IOptionsMonitor<PaymentOptions> _options;
    private readonly ILogger<PaymentProviderRegistrationService> _logger;

    public PaymentProviderRegistrationService(
        IPaymentExecutionContextResolver contextResolver,
        IValidator<RegisterPaymentProviderRequest> validator,
        IPaymentProviderCatalog providerCatalog,
        IAesGcmSecretProtector protector,
        IPaymentRepository repository,
        IOrganizationDirectory organizations,
        IProviderTokenEncryptionKeyRingProvider keyRings,
        IPaymentKeyRingStore keyRingStore,
        IPaymentDistributedLock locks,
        IOptionsMonitor<PaymentOptions> options,
        ILogger<PaymentProviderRegistrationService> logger)
    {
        _contextResolver = contextResolver;
        _validator = validator;
        _providerCatalog = providerCatalog;
        _protector = protector;
        _repository = repository;
        _organizations = organizations;
        _keyRings = keyRings;
        _keyRingStore = keyRingStore;
        _locks = locks;
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

        // The tenant always comes from the caller's context, never the request, so nobody can
        // register a configuration in another tenant. The organization may be named in the
        // request — a console whose context is always the default organization has no other
        // way to configure the rest — but only after IAM confirms it under the caller's own
        // token. Reads deliberately do not follow this: taking the organization from a query
        // would let anyone list another organization's payments by naming it.
        var tenantId = contextResolution.Context!.TenantId;
        var organizationResolution = await ResolveOrganizationAsync(
            request,
            contextResolution.Context,
            correlationId,
            cancellationToken);

        if (organizationResolution.Failure != null)
        {
            return organizationResolution.Failure;
        }

        var organizationId = organizationResolution.OrganizationId;
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

        var scope = new PaymentEncryptionScope(tenantId, organizationId);
        var keyRingFailure = await EnsureKeyRingAsync(
            scope,
            correlationId,
            cancellationToken);

        if (keyRingFailure != null)
        {
            return keyRingFailure;
        }

        // The configuration is encrypted under its own organization's key ring, so it can only
        // be read back by a process resolving that same scope.
        var secrets = await ProtectSecretsAsync(
            scope,
            request,
            cancellationToken);

        if (!secrets.IsProtected)
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
            Version = 1,
            TenantId = tenantId,
            OrganizationId = organizationId,
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
            ProviderSecretsCiphertext = secrets.ProviderCiphertext,
            TenantSecuritySecretsCiphertext = secrets.TenantCiphertext,
            SecretsEncryptionKeyId = secrets.KeyId
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

    /// <summary>
    /// Makes sure this scope has a key ring of its own, creating one if it does not.
    /// </summary>
    /// <returns>A failure to return to the caller, or null to carry on.</returns>
    /// <remarks>
    /// The trigger is "has no ring <em>of its own</em>", not "cannot be read". With
    /// <see cref="PaymentOptions.FallBackToSharedEncryptionKeyRing"/> at its default, an
    /// unprovisioned scope reads perfectly well through the shared ring — so checking
    /// readability alone would never fire, and every new organization would keep landing on
    /// the shared key that scoped rings exist to get away from.
    /// </remarks>
    private async Task<PaymentOperationResult?> EnsureKeyRingAsync(
        PaymentEncryptionScope scope,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.AutoProvisionKeyRing)
        {
            return null;
        }

        var health = await _keyRings.CheckAsync(scope, cancellationToken);

        if (health.IsReadable && !health.UsedSharedKeyRing)
        {
            return null;
        }

        // Two first registrations for the same new organization would otherwise both find
        // nothing and both write, the second overwriting the first — and the first
        // provider's credentials would already be encrypted under the key that just got
        // replaced.
        await using var handle = await _locks.TryAcquireAsync(
            $"payment-keyring:{PaymentKeyRingSecretName.Create(scope)}",
            cancellationToken);

        if (handle == null)
        {
            _logger.LogWarning(
                "Payment key ring provisioning is unavailable Reason=lock_unavailable");

            return PaymentOperationResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_key_ring_unavailable",
                "The encryption key ring could not be provisioned. Try again.",
                correlationId);
        }

        var outcome = await _keyRingStore.TryCreateAsync(
            scope,
            cancellationToken);

        if (outcome == KeyRingProvisionOutcome.Unavailable)
        {
            _logger.LogError(
                "Payment provider registration is unavailable Reason=key_ring_provisioning_failed TenantHash={TenantHash}",
                PaymentLogValue.Hash(scope.TenantId));

            return PaymentOperationResult.Failure(
                PaymentFailureKind.Unavailable,
                "payment_key_ring_unavailable",
                "The encryption key ring could not be provisioned. Try again.",
                correlationId);
        }

        return null;
    }

    /// <summary>
    /// Decides which organization this configuration belongs to.
    /// </summary>
    /// <remarks>
    /// A request naming no organization keeps the original behaviour exactly — the caller's
    /// context decides, and IAM is never called. Only an explicitly named organization is
    /// verified, so the common path costs nothing and gains no new failure mode.
    /// </remarks>
    private async Task<OrganizationResolution> ResolveOrganizationAsync(
        RegisterPaymentProviderRequest request,
        PaymentExecutionContext context,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var requested = request.OrganizationId?.Trim();

        if (string.IsNullOrEmpty(requested))
        {
            return new OrganizationResolution(context.OrganizationId, null);
        }

        // Naming the organization the context already carries proves nothing new, and the
        // token is the stronger evidence, so there is nothing to verify.
        if (string.Equals(
                requested,
                context.OrganizationId,
                StringComparison.Ordinal))
        {
            return new OrganizationResolution(requested, null);
        }

        var outcome = await _organizations.FindAsync(requested, cancellationToken);

        return outcome switch
        {
            OrganizationLookupOutcome.Found =>
                new OrganizationResolution(requested, null),

            OrganizationLookupOutcome.NotFound =>
                new OrganizationResolution(
                    null,
                    PaymentOperationResult.Failure(
                        PaymentFailureKind.Validation,
                        "organization_not_found",
                        "The requested organization does not exist for this tenant.",
                        correlationId)),

            // Fail closed. Writing configuration under an organization nobody could confirm
            // is how a provider ends up encrypted against a key ring that serves the wrong
            // business.
            _ => new OrganizationResolution(
                null,
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Unavailable,
                    "organization_verification_unavailable",
                    "The organization could not be verified. Try again.",
                    correlationId))
        };
    }

    private readonly record struct OrganizationResolution(
        string? OrganizationId,
        PaymentOperationResult? Failure);

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

    private async Task<RegistrationSecrets> ProtectSecretsAsync(
        PaymentEncryptionScope scope,
        RegisterPaymentProviderRequest request,
        CancellationToken cancellationToken)
    {
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

        var providerProtection = await _protector.ProtectAsync(
            scope,
            JsonSerializer.Serialize(credentials, SerializerOptions),
            cancellationToken);
        var tenantProtection = await _protector.ProtectAsync(
            scope,
            JsonSerializer.Serialize(tenantSecurity, SerializerOptions),
            cancellationToken);

        return new RegistrationSecrets(
            providerProtection.IsProtected && tenantProtection.IsProtected,
            providerProtection.Ciphertext,
            tenantProtection.Ciphertext,
            providerProtection.KeyId);
    }

    private static string CreateKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private sealed record RegistrationSecrets(
        bool IsProtected,
        string ProviderCiphertext,
        string TenantCiphertext,
        string KeyId);
}
