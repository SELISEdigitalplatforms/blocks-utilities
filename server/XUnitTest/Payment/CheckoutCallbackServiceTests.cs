using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models.HostedCheckout;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class CheckoutCallbackServiceTests
{
    private const string StateKey = "return-state-key-that-is-longer-than-thirty-two-bytes";

    [Fact]
    public async Task Completed_session_is_validated_persisted_without_raw_result_and_redirected_safely()
    {
        var fixture = new Fixture();
        var payment = fixture.ArrangePayment();
        fixture.Client.Setup(x => x.GetAsync(fixture.Provider, "session-1", "raw-session-result", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutResultClientResult
            {
                Outcome = ProviderClientOutcome.Success,
                Response = new HostedCheckoutResult
                {
                    Id = "session-1",
                    Reference = "payment-1",
                    Status = "completed",
                    Amount = new ProviderAmount { Currency = "USD", Value = 1050 },
                    Payments = [new HostedCheckoutPayment { ResultCode = "Authorised", PspReference = "psp-1" }]
                }
            });
        fixture.Repository.Setup(x => x.SaveCheckoutObservationAsync(
                "tenant-a", "payment-1", "completed", "Authorised",
                It.Is<string>(hash => hash != "raw-session-result" && hash.Length == 64),
                "psp-1", It.IsAny<PaymentInstrument?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await fixture.ProcessAsync("session-1", "raw-session-result");

        result.IsRedirect.Should().BeTrue();
        result.RedirectUrl.Should().Be("https://app-a.example/payment-result?paymentDetailId=payment-1&status=success");
        result.RedirectUrl.Should().NotContain("sessionResult").And.NotContain("psp-1").And.NotContain("tenant-a");
        fixture.Repository.VerifyAll();
        fixture.Client.VerifyAll();
    }

    [Fact]
    public async Task Webhook_authorized_state_takes_precedence_without_calling_the_provider_again()
    {
        var fixture = new Fixture();
        fixture.ArrangePayment(PaymentStatuses.Authorized);

        var result = await fixture.ProcessAsync("session-1", "result");

        result.RedirectUrl.Should().EndWith("paymentDetailId=payment-1&status=success");
        fixture.Client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Session_mismatch_returns_safe_validation_error_without_redirect()
    {
        var fixture = new Fixture();
        fixture.ArrangePayment();

        var result = await fixture.ProcessAsync("different-session", "result");

        result.IsRedirect.Should().BeFalse();
        result.ErrorCode.Should().Be("session_mismatch");
        fixture.Client.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Provider_timeout_redirects_to_pending_using_the_stored_tenant_snapshot()
    {
        var fixture = new Fixture();
        fixture.ArrangePayment();
        fixture.Client.Setup(x => x.GetAsync(fixture.Provider, "session-1", "result", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutResultClientResult { Outcome = ProviderClientOutcome.Timeout });

        var result = await fixture.ProcessAsync("session-1", "result");

        result.RedirectUrl.Should().Be("https://app-a.example/payment-result?paymentDetailId=payment-1&status=pending");
        fixture.Repository.Verify(x => x.SaveCheckoutObservationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<PaymentInstrument?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Missing_provider_amount_redirects_to_pending_when_webhook_is_not_final()
    {
        var fixture = new Fixture();
        fixture.ArrangePayment();
        fixture.Client.Setup(x => x.GetAsync(
                fixture.Provider,
                "session-1",
                "result",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutResultClientResult
            {
                Outcome = ProviderClientOutcome.Success,
                Response = new HostedCheckoutResult
                {
                    Id = "session-1",
                    Reference = "payment-1",
                    Status = "completed"
                }
            });

        var result = await fixture.ProcessAsync("session-1", "result");

        result.RedirectUrl.Should().EndWith(
            "paymentDetailId=payment-1&status=pending");
        result.ErrorCode.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_provider_amount_uses_concurrent_webhook_authorization()
    {
        var fixture = new Fixture();
        var processingPayment = fixture.ArrangePayment();
        var authorizedPayment = new PaymentDetail
        {
            ItemId = processingPayment.ItemId,
            TenantId = processingPayment.TenantId,
            PaymentStatus = PaymentStatuses.Authorized
        };
        fixture.Repository.SetupSequence(x => x.GetByIdAsync(
                "tenant-a",
                "payment-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(processingPayment)
            .ReturnsAsync(authorizedPayment);
        fixture.Client.Setup(x => x.GetAsync(
                fixture.Provider,
                "session-1",
                "result",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutResultClientResult
            {
                Outcome = ProviderClientOutcome.Success,
                Response = new HostedCheckoutResult
                {
                    Id = "session-1",
                    Reference = "payment-1",
                    Status = "completed"
                }
            });

        var result = await fixture.ProcessAsync("session-1", "result");

        result.RedirectUrl.Should().EndWith(
            "paymentDetailId=payment-1&status=success");
    }

    [Fact]
    public async Task Returned_amount_mismatch_is_rejected()
    {
        var fixture = new Fixture();
        fixture.ArrangePayment();
        fixture.Client.Setup(x => x.GetAsync(
                fixture.Provider,
                "session-1",
                "result",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutResultClientResult
            {
                Outcome = ProviderClientOutcome.Success,
                Response = new HostedCheckoutResult
                {
                    Id = "session-1",
                    Reference = "payment-1",
                    Status = "completed",
                    Amount = new ProviderAmount
                    {
                        Currency = "USD",
                        Value = 999
                    }
                }
            });

        var result = await fixture.ProcessAsync("session-1", "result");

        result.IsRedirect.Should().BeFalse();
        result.ErrorCode.Should().Be("payment_mismatch");
    }

    [Fact]
    public async Task Invalid_callback_request_is_rejected_before_rate_limiting()
    {
        var fixture = new Fixture();
        fixture.RequestValidator
            .Setup(x => x.IsValid(It.IsAny<CheckoutCallbackRequest>()))
            .Returns(false);

        var result = await fixture.ProcessAsync("session-1", "result");

        result.ErrorCode.Should().Be("invalid_callback_request");
        fixture.RateLimiter.Verify(
            x => x.CheckAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Rate_limited_callback_is_rejected_before_context_resolution()
    {
        var fixture = new Fixture();
        fixture.ArrangePayment();
        fixture.RateLimiter
            .Setup(x => x.CheckAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRateLimitResult
            {
                IsAvailable = true,
                IsAllowed = false,
                RetryAfterSeconds = 15
            });

        var result = await fixture.ProcessAsync("session-1", "result");

        result.ErrorCode.Should().Be("callback_rate_limit_exceeded");
        result.RetryAfterSeconds.Should().Be(15);
        fixture.Providers.Verify(
            x => x.GetAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Func<Task<PaymentProvider?>>>()),
            Times.Never);
    }

    private sealed class Fixture
    {
        private readonly CheckoutCallbackStateProtector _protector = new();
        public Mock<IPaymentRepository> Repository { get; } = new();
        public Mock<IPaymentProviderCache> Providers { get; } = new();
        public Mock<ICheckoutResultClient> Client { get; } = new();
        public Mock<ICurrencyMinorUnitResolver> MinorUnits { get; } = new();
        public Mock<ICheckoutCallbackRequestValidator> RequestValidator { get; } = new();
        public Mock<ICheckoutCallbackRateLimiter> RateLimiter { get; } = new();
        public PaymentProvider Provider { get; } = new()
        {
            ProviderName = PaymentConstants.AdyenOnlineProvider,
            ReturnStateHmacKey = StateKey,
            ApiBaseUrl = "https://checkout-test.adyen.com/v72",
            IsEnabled = true
        };
        public string StateToken { get; private set; } = string.Empty;

        public Fixture()
        {
            RequestValidator.Setup(x => x.IsValid(It.IsAny<CheckoutCallbackRequest>())).Returns(true);
            RateLimiter.Setup(x => x.CheckAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentRateLimitResult { IsAvailable = true, IsAllowed = true });

            Providers.Setup(x => x.GetAsync(
                    "tenant-a",
                    PaymentConstants.AdyenOnlineProvider,
                    It.IsAny<Func<Task<PaymentProvider?>>>()))
                .ReturnsAsync(Provider);

            long expected = 1050;
            MinorUnits.Setup(x => x.TryConvert(10.50m, "USD", out expected)).Returns(true);
        }

        public CheckoutCallbackService Service => new(
            RequestValidator.Object,
            RateLimiter.Object,
            new CheckoutCallbackContextResolver(
                _protector,
                Repository.Object,
                Providers.Object,
                new CheckoutUrlPolicy()),
            new CheckoutObservationService(
                Client.Object,
                new CheckoutResultValidator(MinorUnits.Object),
                new CheckoutStatusMapper(),
                Repository.Object,
                NullLogger<CheckoutObservationService>.Instance),
            new PaymentRedirectBuilder());

        public Task<CheckoutCallbackResult> ProcessAsync(
            string sessionId,
            string sessionResult) =>
            Service.ProcessAsync(
                new CheckoutCallbackRequest(StateToken, sessionId, sessionResult),
                "127.0.0.1",
                CancellationToken.None);

        public PaymentDetail ArrangePayment(string status = PaymentStatuses.Processing)
        {
            var protectedState = _protector.Create("tenant-a", "payment-1", PaymentConstants.AdyenOnlineProvider, TimeSpan.FromMinutes(30), StateKey);
            StateToken = protectedState.Token;
            var payment = new PaymentDetail
            {
                ItemId = "payment-1",
                TenantId = "tenant-a",
                ProviderName = PaymentConstants.AdyenOnlineProvider,
                PaymentStatus = status,
                SessionId = "session-1",
                PreciseAmount = 10.50m,
                CurrencyCode = "USD",
                FrontendResultUrlSnapshot = "https://app-a.example/payment-result",
                ReturnStateNonceHash = PaymentHashing.HashSensitiveValue(protectedState.State.Nonce)
            };
            Repository.Setup(x => x.GetByIdAsync("tenant-a", "payment-1", It.IsAny<CancellationToken>())).ReturnsAsync(payment);
            return payment;
        }
    }
}
