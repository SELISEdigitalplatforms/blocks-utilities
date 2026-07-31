using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// The three provider administration services share a shape: resolve the
/// tenant, validate, read, write with an optimistic version, then refresh the
/// cache. Each failure along that path has to come back as a distinct kind so
/// the controller can map it onto the right status code.
/// </summary>
public sealed class PaymentProviderAdministrationTests
{
    private const string TenantId = "tenant-1";
    private const string ProviderId = "provider-1";
    private const string CorrelationId = "corr-1";

    private readonly Mock<IPaymentExecutionContextResolver> _contextResolver = new();
    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IPaymentProviderCache> _cache = new();
    private readonly Mock<IPaymentProviderResponseMapper> _responseMapper = new();
    private readonly Mock<IAesGcmSecretProtector> _protector = new();
    private readonly Mock<IProviderCredentialRotationStrategy> _strategy = new();
    private readonly Mock<IValidator<RotatePaymentProviderCredentialsRequest>>
        _rotateValidator = new();
    private readonly Mock<IValidator<UpdatePaymentProviderRequest>>
        _updateValidator = new();

    public PaymentProviderAdministrationTests()
    {
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", null),
                null));
        _rotateValidator.Setup(x => x.ValidateAsync(
                It.IsAny<RotatePaymentProviderCredentialsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _updateValidator.Setup(x => x.ValidateAsync(
                It.IsAny<UpdatePaymentProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _responseMapper.Setup(x => x.Map(It.IsAny<PaymentProvider>()))
            .Returns((PaymentProvider provider) => new PaymentProviderResponse
            {
                PaymentProviderId = provider.ItemId,
                ProviderName = provider.ProviderName,
                Version = provider.Version
            });
        _strategy.Setup(x => x.Supports(It.IsAny<string>())).Returns(true);
        _strategy.Setup(x => x.CreatePlan(
                It.IsAny<PaymentProvider>(),
                It.IsAny<RotatePaymentProviderCredentialsRequest>()))
            .Returns(ProviderCredentialRotationPlan.Success(
                """{"apiKey":"new"}""",
                """{"shopperReferenceHmacKey":"new"}"""));
        _protector.Setup(x => x.TryProtect(
                It.IsAny<string>(),
                out It.Ref<string>.IsAny,
                out It.Ref<string>.IsAny))
            .Returns(
                (string _, out string ciphertext, out string keyId) =>
                {
                    ciphertext = "cipher";
                    keyId = "key-1";

                    return true;
                });
        _cache.Setup(x => x.RefreshAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(Provider());
    }

    private static PaymentProvider Provider(
        string providerName = PaymentConstants.StripeProvider,
        bool isEnabled = true,
        long version = 3) => new()
        {
            ItemId = ProviderId,
            TenantId = TenantId,
            ProviderName = providerName,
            MerchantId = "acct_1",
            IsEnabled = isEnabled,
            Version = version
        };

    private static RotatePaymentProviderCredentialsRequest RotateRequest() =>
        new()
        {
            Version = 3,
            ApiKey = "sk_test_new"
        };

    private static UpdatePaymentProviderRequest UpdateRequest() => new()
    {
        Version = 3,
        FrontendResultUrl = "https://merchant.example/result",
        CountryCode = " ch ",
        StoreId = "  ",
        MaxRefundDays = 30,
        IsEnabled = true
    };

    private PaymentProviderCredentialRotationService RotationService() =>
        new(
            _contextResolver.Object,
            _rotateValidator.Object,
            [_strategy.Object],
            _protector.Object,
            _repository.Object,
            _cache.Object,
            _responseMapper.Object,
            NullLogger<PaymentProviderCredentialRotationService>.Instance);

    private PaymentProviderConfigurationService ConfigurationService() =>
        new(
            _contextResolver.Object,
            _updateValidator.Object,
            _repository.Object,
            _cache.Object,
            _responseMapper.Object,
            NullLogger<PaymentProviderConfigurationService>.Instance);

    private PaymentProviderQueryService QueryService() =>
        new(
            _contextResolver.Object,
            _repository.Object,
            _responseMapper.Object,
            NullLogger<PaymentProviderQueryService>.Instance);

    private void UnresolvableTenant() =>
        _contextResolver.Setup(x => x.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                null,
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Unavailable,
                    "payment_context_missing",
                    "Authenticated tenant context is unavailable.",
                    CorrelationId)));

    private void ExistingProvider(PaymentProvider? provider) =>
        _repository.Setup(x => x.GetProviderByIdAsync(
                TenantId,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(provider);

    private void RotationReturns(PaymentProvider? updated) =>
        _repository.Setup(x => x.TryRotateProviderCredentialsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

    private void UpdateReturns(PaymentProvider? updated) =>
        _repository.Setup(x => x.TryUpdateProviderConfigurationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

    [Fact]
    public async Task Rotation_writes_the_new_ciphertext_under_the_expected_version()
    {
        ExistingProvider(Provider());
        RotationReturns(Provider(version: 4));

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Provider!.Version.Should().Be(4);
        _repository.Verify(
            x => x.TryRotateProviderCredentialsAsync(
                TenantId,
                ProviderId,
                3,
                "cipher",
                "cipher",
                "key-1",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Rotation_refreshes_the_provider_cache()
    {
        ExistingProvider(Provider());
        RotationReturns(Provider(version: 4));

        await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        _cache.Verify(
            x => x.Remove(TenantId, PaymentConstants.StripeProvider),
            Times.Once);
        _cache.Verify(
            x => x.RefreshAsync(
                TenantId,
                PaymentConstants.StripeProvider,
                It.IsAny<Func<Task<PaymentProvider?>>>()),
            Times.Once);
    }

    [Fact]
    public async Task Rotation_survives_a_cache_refresh_failure()
    {
        ExistingProvider(Provider());
        RotationReturns(Provider(version: 4));
        _cache.Setup(x => x.RefreshAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        // The credentials are already rotated; a cold cache is not a failure.
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Rotation_reports_success_even_when_the_refresh_finds_nothing()
    {
        ExistingProvider(Provider());
        RotationReturns(Provider(version: 4));
        _cache.Setup(x => x.RefreshAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync((PaymentProvider?)null);

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Rotation_requires_a_request()
    {
        var act = () => RotationService().RotateAsync(
            ProviderId,
            null!,
            CorrelationId,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Rotation_fails_when_the_tenant_cannot_be_resolved()
    {
        UnresolvableTenant();

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_context_missing");
        _repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rotation_rejects_a_missing_provider_id(string providerId)
    {
        var result = await RotationService().RotateAsync(
            providerId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("payment_provider_id_invalid");
    }

    [Fact]
    public async Task Rotation_reports_the_first_validation_failure()
    {
        _rotateValidator.Setup(x => x.ValidateAsync(
                It.IsAny<RotatePaymentProviderCredentialsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(
            [
                new ValidationFailure("Version", "Version is required.")
                {
                    ErrorCode = "payment_provider_version_required"
                }
            ]));

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("payment_provider_version_required");
        result.ErrorMessage.Should().Be("Version is required.");
    }

    [Fact]
    public async Task Rotation_falls_back_to_a_generic_code_when_the_rule_has_none()
    {
        _rotateValidator.Setup(x => x.ValidateAsync(
                It.IsAny<RotatePaymentProviderCredentialsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(
            [
                new ValidationFailure("ApiKey", "Provide at least one credential.")
            ]));

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_rotation_invalid");
    }

    [Fact]
    public async Task Rotation_reports_a_missing_provider_as_not_found()
    {
        ExistingProvider(null);

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should().Be("payment_provider_not_found");
    }

    [Fact]
    public async Task Rotation_is_unavailable_for_a_provider_with_no_strategy()
    {
        ExistingProvider(Provider("paypal"));
        _strategy.Setup(x => x.Supports(It.IsAny<string>())).Returns(false);

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_rotation_unsupported");
    }

    [Fact]
    public async Task A_strategy_that_refuses_the_request_is_reported_verbatim()
    {
        ExistingProvider(Provider());
        _strategy.Setup(x => x.CreatePlan(
                It.IsAny<PaymentProvider>(),
                It.IsAny<RotatePaymentProviderCredentialsRequest>()))
            .Returns(ProviderCredentialRotationPlan.Failure(
                PaymentFailureKind.Validation,
                "payment_provider_hmac_invalid",
                "The webhook key is not valid hex."));

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("payment_provider_hmac_invalid");
    }

    [Fact]
    public async Task Rotation_fails_closed_when_the_credentials_cannot_be_encrypted()
    {
        ExistingProvider(Provider());
        _protector.Setup(x => x.TryProtect(
                It.IsAny<string>(),
                out It.Ref<string>.IsAny,
                out It.Ref<string>.IsAny))
            .Returns(
                (string _, out string ciphertext, out string keyId) =>
                {
                    ciphertext = string.Empty;
                    keyId = string.Empty;

                    return false;
                });

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_rotation_unavailable");
        _repository.Verify(
            x => x.TryRotateProviderCredentialsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Rotation_fails_closed_when_the_two_halves_land_on_different_keys()
    {
        // Storing the credential under one key and the tenant security material
        // under another leaves the provider undecryptable after a key rollover.
        ExistingProvider(Provider());
        var call = 0;
        _protector.Setup(x => x.TryProtect(
                It.IsAny<string>(),
                out It.Ref<string>.IsAny,
                out It.Ref<string>.IsAny))
            .Returns(
                (string _, out string ciphertext, out string keyId) =>
                {
                    ciphertext = "cipher";
                    keyId = call++ == 0 ? "key-1" : "key-2";

                    return true;
                });

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_rotation_unavailable");
    }

    [Fact]
    public async Task Rotation_reports_a_stale_version_as_a_conflict()
    {
        ExistingProvider(Provider());
        RotationReturns(null);

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("payment_provider_version_conflict");
    }

    [Fact]
    public async Task An_unreadable_store_makes_rotation_unavailable()
    {
        _repository.Setup(x => x.GetProviderByIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("mongo down"));

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_store_unavailable");
    }

    [Fact]
    public async Task An_unwritable_store_makes_rotation_unavailable()
    {
        ExistingProvider(Provider());
        _repository.Setup(x => x.TryRotateProviderCredentialsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("mongo down"));

        var result = await RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_store_unavailable");
    }

    [Fact]
    public async Task A_caller_cancellation_during_rotation_is_propagated()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _repository.Setup(x => x.GetProviderByIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => RotationService().RotateAsync(
            ProviderId,
            RotateRequest(),
            CorrelationId,
            source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Configuration_normalises_the_country_and_store_before_writing()
    {
        ExistingProvider(Provider());
        UpdateReturns(Provider(version: 4));

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _repository.Verify(
            x => x.TryUpdateProviderConfigurationAsync(
                TenantId,
                ProviderId,
                3,
                "https://merchant.example/result",
                "CH",
                false,
                30,
                null,
                true,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Configuration_refreshes_the_provider_cache()
    {
        ExistingProvider(Provider());
        UpdateReturns(Provider(version: 4));

        await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        _cache.Verify(
            x => x.Remove(TenantId, PaymentConstants.StripeProvider),
            Times.Once);
    }

    [Fact]
    public async Task Disabling_a_provider_does_not_expect_it_back_in_the_cache()
    {
        ExistingProvider(Provider());
        UpdateReturns(Provider(isEnabled: false, version: 4));
        _cache.Setup(x => x.RefreshAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync((PaymentProvider?)null);

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Configuration_survives_a_cache_refresh_failure()
    {
        ExistingProvider(Provider());
        UpdateReturns(Provider(version: 4));
        _cache.Setup(x => x.RefreshAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Configuration_requires_a_request()
    {
        var act = () => ConfigurationService().UpdateAsync(
            ProviderId,
            null!,
            CorrelationId,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Configuration_fails_when_the_tenant_cannot_be_resolved()
    {
        UnresolvableTenant();

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_context_missing");
    }

    [Fact]
    public async Task Configuration_rejects_a_missing_provider_id()
    {
        var result = await ConfigurationService().UpdateAsync(
            "  ",
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_id_invalid");
    }

    [Fact]
    public async Task Configuration_reports_the_first_validation_failure()
    {
        _updateValidator.Setup(x => x.ValidateAsync(
                It.IsAny<UpdatePaymentProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(
            [
                new ValidationFailure("FrontendResultUrl", "A result url is required.")
            ]));

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_request_invalid");
        result.ErrorMessage.Should().Be("A result url is required.");
    }

    [Fact]
    public async Task Configuration_keeps_a_rule_supplied_error_code()
    {
        _updateValidator.Setup(x => x.ValidateAsync(
                It.IsAny<UpdatePaymentProviderRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(
            [
                new ValidationFailure("CountryCode", "Not a country.")
                {
                    ErrorCode = "payment_provider_country_invalid"
                }
            ]));

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_country_invalid");
    }

    [Fact]
    public async Task Configuration_reports_a_missing_provider_as_not_found()
    {
        ExistingProvider(null);

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
    }

    [Fact]
    public async Task Configuration_reports_a_stale_version_as_a_conflict()
    {
        ExistingProvider(Provider());
        UpdateReturns(null);

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be("payment_provider_version_conflict");
    }

    [Fact]
    public async Task An_unreadable_store_makes_configuration_unavailable()
    {
        _repository.Setup(x => x.GetProviderByIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("mongo down"));

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_store_unavailable");
    }

    [Fact]
    public async Task An_unwritable_store_makes_configuration_unavailable()
    {
        ExistingProvider(Provider());
        _repository.Setup(x => x.TryUpdateProviderConfigurationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("mongo down"));

        var result = await ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be("payment_provider_store_unavailable");
    }

    [Fact]
    public async Task A_caller_cancellation_during_configuration_is_propagated()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _repository.Setup(x => x.GetProviderByIdAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => ConfigurationService().UpdateAsync(
            ProviderId,
            UpdateRequest(),
            CorrelationId,
            source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Listing_orders_providers_by_name_then_merchant_then_id()
    {
        _repository.Setup(x => x.GetProvidersAsync(
                TenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PaymentProvider
                {
                    ItemId = "p3",
                    ProviderName = "stripe",
                    MerchantId = "acct_1"
                },
                new PaymentProvider
                {
                    ItemId = "p1",
                    ProviderName = "adyen-online",
                    MerchantId = "merchant-b"
                },
                new PaymentProvider
                {
                    ItemId = "p2",
                    ProviderName = "adyen-online",
                    MerchantId = "merchant-a"
                }
            ]);

        var result = await QueryService().GetProvidersAsync(
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Providers.Select(provider => provider.PaymentProviderId)
            .Should().Equal("p2", "p1", "p3");
    }

    [Fact]
    public async Task Listing_an_empty_tenant_returns_an_empty_list()
    {
        _repository.Setup(x => x.GetProvidersAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await QueryService().GetProvidersAsync(
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Providers.Should().BeEmpty();
    }

    [Fact]
    public async Task Listing_fails_when_the_tenant_cannot_be_resolved()
    {
        UnresolvableTenant();

        var result = await QueryService().GetProvidersAsync(
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_context_missing");
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unreadable_store_makes_listing_unavailable()
    {
        _repository.Setup(x => x.GetProvidersAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("mongo down"));

        var result = await QueryService().GetProvidersAsync(
            CorrelationId,
            CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_provider_store_unavailable");
    }

    [Fact]
    public async Task A_caller_cancellation_during_listing_is_propagated()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _repository.Setup(x => x.GetProvidersAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var act = () => QueryService().GetProvidersAsync(
            CorrelationId,
            source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
