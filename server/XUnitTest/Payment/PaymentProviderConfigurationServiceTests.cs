using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Validators;

namespace XUnitTest.Payment;

public sealed class PaymentProviderConfigurationServiceTests
{
    [Fact]
    public async Task Update_requires_an_explicit_version()
    {
        var validator = new UpdatePaymentProviderRequestValidator(
            new CheckoutUrlPolicy());
        var request = new UpdatePaymentProviderRequest
        {
            FrontendResultUrl = "https://app.example.com/payment-result"
        };

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(request.Version));
    }

    private const string TenantId = "tenant-1";

    private readonly Mock<IPaymentRepository> _repository = new();
    private readonly Mock<IPaymentProviderCache> _cache = new();
    private readonly Mock<IPaymentExecutionContextResolver>
        _contextResolver = new();

    public PaymentProviderConfigurationServiceTests()
    {
        _contextResolver.Setup(resolver =>
                resolver.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(
                    TenantId,
                    "actor-1",
                    null),
                null));
    }

    [Fact]
    public async Task Update_uses_version_compare_and_set_and_refreshes_cache()
    {
        var current = Provider(version: 5);
        var updated = Provider(version: 6);
        updated.FrontendResultUrl =
            "https://client.example/new-result";
        updated.IsEnabled = false;

        _repository.Setup(repository =>
                repository.GetProviderByIdAsync(
                    TenantId,
                    current.ItemId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        _repository.Setup(repository =>
                repository.TryUpdateProviderConfigurationAsync(
                    TenantId,
                    current.ItemId,
                    5,
                    "https://client.example/new-result",
                    "CH",
                    true,
                    90,
                    "store-1",
                    false,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var result = await Service().UpdateAsync(
            current.ItemId,
            new UpdatePaymentProviderRequest
            {
                Version = 5,
                FrontendResultUrl =
                    "https://client.example/new-result",
                CountryCode = "ch",
                ManualCapture = true,
                MaxRefundDays = 90,
                StoreId = "store-1",
                IsEnabled = false
            },
            "corr",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Provider!.Version.Should().Be(6);
        _cache.Verify(cache => cache.Remove(
            TenantId,
            current.ProviderName), Times.Once);
        _cache.Verify(cache => cache.RefreshAsync(
            TenantId,
            current.ProviderName,
            It.IsAny<Func<Task<PaymentProvider?>>>()), Times.Once);
    }

    [Fact]
    public async Task A_stale_version_returns_conflict()
    {
        var current = Provider(version: 5);

        _repository.Setup(repository =>
                repository.GetProviderByIdAsync(
                    TenantId,
                    current.ItemId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        _repository.Setup(repository =>
                repository.TryUpdateProviderConfigurationAsync(
                    TenantId,
                    current.ItemId,
                    4,
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<bool>(),
                    It.IsAny<int>(),
                    It.IsAny<string?>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentProvider?)null);

        var result = await Service().UpdateAsync(
            current.ItemId,
            new UpdatePaymentProviderRequest
            {
                Version = 4,
                FrontendResultUrl =
                    "https://client.example/result",
                IsEnabled = true
            },
            "corr",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(
            PaymentFailureKind.Conflict);
        result.ErrorCode.Should().Be(
            "payment_provider_version_conflict");
        _cache.Verify(cache => cache.Remove(
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Provider_identity_fields_are_rejected_not_silently_ignored()
    {
        var request = JsonSerializer.Deserialize<
            UpdatePaymentProviderRequest>(
                """
                {
                  "version": 3,
                  "frontendResultUrl": "https://client.example/result",
                  "isEnabled": true,
                  "providerName": "STRIPE"
                }
                """,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web))!;

        var validation =
            new UpdatePaymentProviderRequestValidator(
                    new CheckoutUrlPolicy())
                .Validate(request);

        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().Contain(error =>
            error.ErrorCode ==
            "payment_provider_identity_immutable");
    }

    private PaymentProviderConfigurationService Service() =>
        new(
            _contextResolver.Object,
            new UpdatePaymentProviderRequestValidator(
                new CheckoutUrlPolicy()),
            _repository.Object,
            _cache.Object,
            new PaymentProviderResponseMapper(),
            NullLogger<
                PaymentProviderConfigurationService>.Instance);

    private static PaymentProvider Provider(long version) =>
        new()
        {
            ItemId = "provider-1",
            Version = version,
            TenantId = TenantId,
            ProviderName = "ADYEN-ONLINE",
            MerchantId = "merchant-1",
            ApiBaseUrl =
                "https://checkout-test.adyen.com/v72",
            FrontendResultUrl =
                "https://client.example/result",
            IsEnabled = true
        };
}
