using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public class PaymentEncryptionAdminServiceTests
{
    private readonly Mock<IPaymentExecutionContextResolver> _contextResolver = new();
    private readonly Mock<IProviderTokenEncryptionKeyRingProvider> _keyRings = new();
    private readonly Mock<IPaymentSecretReEncryptionService> _reEncryption = new();
    private readonly PaymentEncryptionAdminService _service;

    private const string Correlation = "corr-1";

    private static readonly PaymentExecutionContext Context =
        new("tenant-1", "actor-1", "org-1", "user-1");

    public PaymentEncryptionAdminServiceTests()
    {
        _service = new PaymentEncryptionAdminService(
            _contextResolver.Object,
            _keyRings.Object,
            _reEncryption.Object,
            Mock.Of<ILogger<PaymentEncryptionAdminService>>());
    }

    private void ResolvesTo(PaymentExecutionContext context) =>
        _contextResolver
            .Setup(c => c.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(context, null));

    private void FailsToResolve(PaymentFailureKind kind, string code, string message) =>
        _contextResolver
            .Setup(c => c.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                null,
                PaymentOperationResult.Failure(kind, code, message, Correlation)));

    private void RingReports(PaymentKeyRingHealth health) =>
        _keyRings
            .Setup(k => k.CheckAsync(It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(health);

    private static PaymentKeyRingHealth Healthy(bool usedSharedKeyRing = false) =>
        new(true, "payment-keys-org-1", usedSharedKeyRing, "key-2", string.Empty);

    [Fact]
    public async Task GetHealth_reports_the_ring_when_the_caller_context_resolves()
    {
        ResolvesTo(Context);
        RingReports(Healthy());

        var result = await _service.GetHealthAsync(Correlation, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.FailureKind.Should().Be(PaymentFailureKind.None);
        result.CorrelationId.Should().Be(Correlation);
        result.Health!.SecretName.Should().Be("payment-keys-org-1");
        result.Health.IsReadable.Should().BeTrue();
        result.Health.ActiveKeyId.Should().Be("key-2");
    }

    [Fact]
    public async Task GetHealth_carries_an_unreadable_ring_through_as_a_successful_diagnostic()
    {
        // An unreadable ring is a valid answer to "is this ring healthy", not a failed call.
        ResolvesTo(Context);
        RingReports(new(false, "payment-keys-org-1", false, string.Empty, "vault returned 403"));

        var result = await _service.GetHealthAsync(Correlation, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Health!.IsReadable.Should().BeFalse();
        result.Health.FailureReason.Should().Be("vault returned 403");
    }

    [Fact]
    public async Task GetHealth_surfaces_the_shared_ring_so_an_operator_can_see_it_is_unprovisioned()
    {
        ResolvesTo(Context);
        RingReports(Healthy(usedSharedKeyRing: true));

        var result = await _service.GetHealthAsync(Correlation, CancellationToken.None);

        result.Health!.UsesSharedKeyRing.Should().BeTrue();
    }

    [Fact]
    public async Task GetHealth_returns_the_resolver_failure_without_touching_the_key_ring()
    {
        FailsToResolve(PaymentFailureKind.Validation, "unauthorized", "no tenant on the request");

        var result = await _service.GetHealthAsync(Correlation, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Health.Should().BeNull();
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("unauthorized");
        result.ErrorMessage.Should().Be("no tenant on the request");
        _keyRings.Verify(
            k => k.CheckAsync(It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReEncrypt_reports_what_moved_when_the_ring_is_the_organization_own()
    {
        ResolvesTo(Context);
        RingReports(Healthy());
        _reEncryption
            .Setup(r => r.ReEncryptAsync(It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentSecretReEncryptionSummary(3, 7, 2, 1));

        var result = await _service.ReEncryptAsync(Correlation, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.FailureKind.Should().Be(PaymentFailureKind.None);
        result.Summary!.ProvidersReEncrypted.Should().Be(3);
        result.Summary.StoredPaymentMethodsReEncrypted.Should().Be(7);
        result.Summary.Skipped.Should().Be(2);
        result.Summary.Failed.Should().Be(1);
    }

    [Fact]
    public async Task ReEncrypt_refuses_when_the_ring_cannot_be_read()
    {
        ResolvesTo(Context);
        RingReports(new(false, "payment-keys-org-1", false, string.Empty, "vault unreachable"));

        var result = await _service.ReEncryptAsync(Correlation, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Summary.Should().BeNull();
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_encryption_key_ring_unavailable");
        _reEncryption.Verify(
            r => r.ReEncryptAsync(It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReEncrypt_refuses_on_the_shared_ring_rather_than_reporting_a_migration_that_moved_nothing()
    {
        // The refusal is the point: re-encrypting onto the shared ring would succeed and move
        // nothing, which reads as a completed migration.
        ResolvesTo(Context);
        RingReports(Healthy(usedSharedKeyRing: true));

        var result = await _service.ReEncryptAsync(Correlation, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_encryption_key_ring_not_provisioned");
        _reEncryption.Verify(
            r => r.ReEncryptAsync(It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReEncrypt_returns_the_resolver_failure_without_reading_the_ring()
    {
        FailsToResolve(PaymentFailureKind.Validation, "unauthorized", "no tenant on the request");

        var result = await _service.ReEncryptAsync(Correlation, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("unauthorized");
        _keyRings.Verify(
            k => k.CheckAsync(It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Both_operations_scope_to_the_caller_own_tenant_and_organization()
    {
        // The scope is derived, never passed in, so this is what stops one tenant rewriting
        // another tenant's ciphertext under its own key.
        ResolvesTo(Context);
        RingReports(Healthy());
        _reEncryption
            .Setup(r => r.ReEncryptAsync(It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentSecretReEncryptionSummary(0, 0, 0, 0));

        await _service.GetHealthAsync(Correlation, CancellationToken.None);
        await _service.ReEncryptAsync(Correlation, CancellationToken.None);

        var expected = new PaymentEncryptionScope("tenant-1", "org-1");
        _keyRings.Verify(k => k.CheckAsync(expected, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _reEncryption.Verify(r => r.ReEncryptAsync(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_caller_with_no_organization_still_scopes_to_its_tenant()
    {
        // Machine-to-machine callers authenticate without an organization; the scope has to
        // remain well formed rather than falling back to some other tenant.
        ResolvesTo(new("tenant-2", "actor-2", null));
        RingReports(Healthy());

        await _service.GetHealthAsync(Correlation, CancellationToken.None);

        _keyRings.Verify(
            k => k.CheckAsync(new PaymentEncryptionScope("tenant-2", null), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task The_cancellation_token_reaches_both_collaborators()
    {
        using var cts = new CancellationTokenSource();
        ResolvesTo(Context);
        RingReports(Healthy());
        _reEncryption
            .Setup(r => r.ReEncryptAsync(It.IsAny<PaymentEncryptionScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentSecretReEncryptionSummary(0, 0, 0, 0));

        await _service.ReEncryptAsync(Correlation, cts.Token);

        _keyRings.Verify(k => k.CheckAsync(It.IsAny<PaymentEncryptionScope>(), cts.Token), Times.Once);
        _reEncryption.Verify(r => r.ReEncryptAsync(It.IsAny<PaymentEncryptionScope>(), cts.Token), Times.Once);
    }
}
