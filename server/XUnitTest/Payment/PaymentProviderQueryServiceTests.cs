using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

public sealed class PaymentProviderQueryServiceTests
{
    [Fact]
    public async Task Listing_is_tenant_scoped_and_never_serializes_secrets()
    {
        const string tenantId = "tenant-1";
        var contextResolver =
            new Mock<IPaymentExecutionContextResolver>();
        contextResolver.Setup(resolver =>
                resolver.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(
                    tenantId,
                    "actor-1",
                    null),
                null));

        var repository = new Mock<IPaymentRepository>();
        repository.Setup(item => item.GetProvidersAsync(
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PaymentProvider
                {
                    ItemId = "provider-1",
                    Version = 3,
                    TenantId = tenantId,
                    ProviderName = "ADYEN-ONLINE",
                    MerchantId = "merchant-1",
                    ApiBaseUrl =
                        "https://checkout-test.adyen.com/v72",
                    ProviderSecretsCiphertext =
                        "credential-ciphertext",
                    TenantSecuritySecretsCiphertext =
                        "tenant-ciphertext",
                    ApiKey = "plaintext-api-key",
                    StandardWebhookHmacKey =
                        "plaintext-webhook-key",
                    IsEnabled = true
                }
            ]);

        var service = new PaymentProviderQueryService(
            contextResolver.Object,
            repository.Object,
            new PaymentProviderResponseMapper(),
            NullLogger<PaymentProviderQueryService>.Instance);

        var result = await service.GetProvidersAsync(
            "corr",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Providers.Should().ContainSingle();
        var serialized = JsonSerializer.Serialize(
            result.Providers);
        serialized.Should()
            .NotContain("credential-ciphertext")
            .And.NotContain("tenant-ciphertext")
            .And.NotContain("plaintext-api-key")
            .And.NotContain("plaintext-webhook-key")
            .And.NotContain(tenantId);
        repository.Verify(item => item.GetProvidersAsync(
            tenantId,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
