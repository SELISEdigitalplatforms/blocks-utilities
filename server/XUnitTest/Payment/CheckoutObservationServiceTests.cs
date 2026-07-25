using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class CheckoutObservationServiceTests
{
    private const string TenantId = "tenant-1";
    private const string PaymentId = "payment-1";

    private sealed class Harness
    {
        public Mock<ICheckoutResultClient> Client { get; } = new();
        public Mock<ICheckoutResultValidator> Validator { get; } = new();
        public Mock<ICheckoutStatusMapper> StatusMapper { get; } = new();
        public Mock<IPaymentRepository> Repository { get; } = new();
        public CheckoutObservationService Service { get; }

        public Harness()
        {
            Service = new CheckoutObservationService(
                Client.Object,
                Validator.Object,
                StatusMapper.Object,
                Repository.Object,
                NullLogger<CheckoutObservationService>.Instance);
        }

        public CheckoutCallbackContext Context()
        {
            var state = new CheckoutCallbackState(
                TenantId, PaymentId, "ADYEN-ONLINE",
                DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5), "nonce");
            var provider = new PaymentProvider
            {
                TenantId = TenantId,
                ProviderName = "ADYEN-ONLINE"
            };
            var payment = new PaymentDetail
            {
                ItemId = PaymentId,
                TenantId = TenantId,
                SessionId = "session-1"
            };
            return new CheckoutCallbackContext(state, provider, payment);
        }

        public void ArrangeClient(ProviderClientOutcome outcome, HostedCheckoutResult? response)
        {
            Client.Setup(c => c.GetAsync(
                    It.IsAny<PaymentProvider>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CheckoutResultClientResult
                {
                    Outcome = outcome,
                    Response = response
                });
        }
    }

    [Fact]
    public async Task Rejected_provider_result_is_an_invalid_session_result()
    {
        var harness = new Harness();
        harness.ArrangeClient(ProviderClientOutcome.Rejected, null);

        var result = await harness.Service.ObserveAsync(
            harness.Context(), "session-result", CancellationToken.None);

        result.RedirectStatus.Should().BeNull();
        result.Failure!.ErrorCode.Should().Be("invalid_session_result");
    }

    [Fact]
    public async Task Non_success_outcome_is_reported_as_pending()
    {
        var harness = new Harness();
        harness.ArrangeClient(ProviderClientOutcome.Timeout, null);

        var result = await harness.Service.ObserveAsync(
            harness.Context(), "session-result", CancellationToken.None);

        result.RedirectStatus.Should().Be(PaymentRedirectStatuses.Pending);
    }

    [Fact]
    public async Task Mismatched_provider_result_is_rejected()
    {
        var harness = new Harness();
        harness.ArrangeClient(
            ProviderClientOutcome.Success,
            new HostedCheckoutResult { Status = "completed" });
        harness.Validator.Setup(v => v.Validate(
                It.IsAny<PaymentDetail>(), It.IsAny<HostedCheckoutResult>()))
            .Returns(CheckoutResultValidationOutcome.Mismatch);

        var result = await harness.Service.ObserveAsync(
            harness.Context(), "session-result", CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("payment_mismatch");
    }

    [Fact]
    public async Task Saved_observation_returns_mapped_redirect_status()
    {
        var harness = new Harness();
        harness.ArrangeClient(
            ProviderClientOutcome.Success,
            new HostedCheckoutResult { Status = "completed" });
        harness.Validator.Setup(v => v.Validate(
                It.IsAny<PaymentDetail>(), It.IsAny<HostedCheckoutResult>()))
            .Returns(CheckoutResultValidationOutcome.Valid);
        harness.StatusMapper.Setup(m => m.Normalize("completed")).Returns("completed");
        harness.StatusMapper.Setup(m => m.ToRedirectStatus("completed"))
            .Returns(PaymentRedirectStatuses.Success);
        harness.Repository.Setup(r => r.SaveCheckoutObservationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<PaymentInstrument?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await harness.Service.ObserveAsync(
            harness.Context(), "session-result", CancellationToken.None);

        result.RedirectStatus.Should().Be(PaymentRedirectStatuses.Success);
    }

    [Fact]
    public async Task Unsaved_observation_falls_back_to_authoritative_failure_status()
    {
        var harness = new Harness();
        harness.ArrangeClient(
            ProviderClientOutcome.Success,
            new HostedCheckoutResult { Status = "canceled" });
        harness.Validator.Setup(v => v.Validate(
                It.IsAny<PaymentDetail>(), It.IsAny<HostedCheckoutResult>()))
            .Returns(CheckoutResultValidationOutcome.Valid);
        harness.StatusMapper.Setup(m => m.Normalize(It.IsAny<string>())).Returns("canceled");
        harness.Repository.Setup(r => r.SaveCheckoutObservationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<PaymentInstrument?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        harness.Repository.Setup(r => r.GetByIdAsync(
                TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = PaymentId,
                TenantId = TenantId,
                PaymentStatus = PaymentStatuses.Cancelled
            });

        var result = await harness.Service.ObserveAsync(
            harness.Context(), "session-result", CancellationToken.None);

        result.RedirectStatus.Should().Be(PaymentRedirectStatuses.Fail);
    }

    [Fact]
    public async Task Unsaved_observation_without_resolvable_state_is_unavailable()
    {
        var harness = new Harness();
        harness.ArrangeClient(
            ProviderClientOutcome.Success,
            new HostedCheckoutResult { Status = "paymentPending" });
        harness.Validator.Setup(v => v.Validate(
                It.IsAny<PaymentDetail>(), It.IsAny<HostedCheckoutResult>()))
            .Returns(CheckoutResultValidationOutcome.Valid);
        harness.StatusMapper.Setup(m => m.Normalize(It.IsAny<string>())).Returns("paymentPending");
        harness.Repository.Setup(r => r.SaveCheckoutObservationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<PaymentInstrument?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        harness.Repository.Setup(r => r.GetByIdAsync(
                TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = PaymentId,
                TenantId = TenantId,
                PaymentStatus = PaymentStatuses.Processing
            });

        var result = await harness.Service.ObserveAsync(
            harness.Context(), "session-result", CancellationToken.None);

        result.Failure!.ErrorCode.Should().Be("payment_observation_unavailable");
    }

    [Fact]
    public async Task Provider_data_unavailable_resolves_authoritative_success()
    {
        var harness = new Harness();
        harness.ArrangeClient(
            ProviderClientOutcome.Success,
            new HostedCheckoutResult { Status = "completed" });
        harness.Validator.Setup(v => v.Validate(
                It.IsAny<PaymentDetail>(), It.IsAny<HostedCheckoutResult>()))
            .Returns(CheckoutResultValidationOutcome.ProviderDataUnavailable);
        harness.Repository.Setup(r => r.GetByIdAsync(
                TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = PaymentId,
                TenantId = TenantId,
                PaymentStatus = PaymentStatuses.Authorized
            });

        var result = await harness.Service.ObserveAsync(
            harness.Context(), "session-result", CancellationToken.None);

        result.RedirectStatus.Should().Be(PaymentRedirectStatuses.Success);
    }
}
