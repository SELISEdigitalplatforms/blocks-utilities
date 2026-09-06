using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// The provider reconciler is what turns "the retry budget is spent" into a real decision instead
/// of a guess: it reads Stripe directly and, only when Stripe has decided, applies the outcome
/// through the exact same transition services a genuine webhook uses -- never anything of its
/// own invention.
/// </summary>
public sealed class StripeCheckoutReconciliationServiceTests
{
    private const string TenantId = "tenant-1";
    private const string PaymentId = "pay-1";

    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentProviderCache> _providerCache = new();
    private readonly Mock<IHttpService> _http = new();
    private readonly Mock<IPaymentWebhookStateTransitionService> _chargeTransitions = new();
    private readonly Mock<IPaymentMethodSetupWebhookStateTransitionService> _setupTransitions = new();

    private static IOptionsMonitor<PaymentOptions> Options() =>
        new PaymentOptionsMonitorStub(new PaymentOptions { ProviderTimeoutSeconds = 15 });

    private StripeCheckoutReconciliationService Service() => new(
        _payments.Object,
        _providerCache.Object,
        _http.Object,
        new StripeEndpointPolicy(),
        Options(),
        _chargeTransitions.Object,
        _setupTransitions.Object,
        NullLogger<StripeCheckoutReconciliationService>.Instance);

    private static PaymentDetail Payment(
        string paymentFlow = PaymentFlows.HostedCheckout,
        string providerName = PaymentConstants.StripeProvider,
        string? sessionId = "cs_test_1",
        string? shopperReference = "shopper-1") => new()
    {
        ItemId = PaymentId,
        TenantId = TenantId,
        OrganizationId = "org-1",
        ProviderName = providerName,
        PaymentFlow = paymentFlow,
        SessionId = sessionId,
        ShopperReference = shopperReference,
        CurrencyCode = "EUR",
        PreciseAmount = 25.00m,
        CorrelationId = "corr-1"
    };

    private static PaymentProvider Provider(string apiBaseUrl = "https://api.stripe.com") => new()
    {
        ProviderName = PaymentConstants.StripeProvider,
        ApiBaseUrl = apiBaseUrl,
        ApiKey = "sk_test_secret",
        MerchantId = "acct_1"
    };

    private void GivenPayment(PaymentDetail payment) =>
        _payments
            .Setup(repository => repository.GetByIdAsync(TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

    private void GivenProvider(PaymentProvider? provider) =>
        _providerCache
            .Setup(cache => cache.GetAsync(
                TenantId,
                It.IsAny<string?>(),
                PaymentConstants.StripeProvider,
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(provider);

    private void GivenSessionRead(StripeCheckoutSessionReconciliation? session, string error = "") =>
        _http
            .Setup(x => x.SendRequest<StripeCheckoutSessionReconciliation>(
                It.IsAny<HttpMethod>(),
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>()))
            .ReturnsAsync((session!, error));

    [Fact]
    public async Task A_provider_this_cannot_observe_is_left_undecided()
    {
        GivenPayment(Payment(providerName: PaymentConstants.AdyenOnlineProvider));

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeFalse();
        _http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_payment_with_no_session_id_is_left_undecided()
    {
        GivenPayment(Payment(sessionId: null));

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeFalse();
        _http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_unsafe_provider_endpoint_is_left_undecided()
    {
        GivenPayment(Payment());
        GivenProvider(Provider("http://169.254.169.254/latest"));

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeFalse();
        _http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_still_open_session_is_left_undecided()
    {
        GivenPayment(Payment());
        GivenProvider(Provider());
        GivenSessionRead(new StripeCheckoutSessionReconciliation
        {
            Id = "cs_test_1",
            Status = "open",
            PaymentStatus = "unpaid"
        });

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeFalse();
        _chargeTransitions.VerifyNoOtherCalls();
        _setupTransitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_session_id_mismatch_is_left_undecided()
    {
        GivenPayment(Payment());
        GivenProvider(Provider());
        GivenSessionRead(new StripeCheckoutSessionReconciliation
        {
            Id = "cs_test_someone_elses",
            Status = "complete",
            PaymentStatus = "paid"
        });

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeFalse();
        _chargeTransitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_completed_charge_with_no_intent_is_left_undecided()
    {
        GivenPayment(Payment());
        GivenProvider(Provider());
        GivenSessionRead(new StripeCheckoutSessionReconciliation
        {
            Id = "cs_test_1",
            Status = "complete",
            PaymentStatus = "paid",
            AmountTotal = 2500,
            Currency = "eur"
        });

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeFalse();
        _chargeTransitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_completed_charge_is_applied_through_the_charge_transition_path()
    {
        PaymentWebhookInbox? applied = null;
        GivenPayment(Payment());
        GivenProvider(Provider());
        GivenSessionRead(new StripeCheckoutSessionReconciliation
        {
            Id = "cs_test_1",
            Status = "complete",
            PaymentStatus = "paid",
            AmountTotal = 2500,
            Currency = "eur",
            PaymentIntent = new StripeReconciliationIntent
            {
                Id = "pi_1",
                Status = "succeeded",
                Customer = "cus_1",
                PaymentMethod = new StripeReconciliationPaymentMethod
                {
                    Id = "pm_1",
                    Type = "card",
                    Card = new StripeReconciliationCard { Brand = "visa", Last4 = "4242" }
                }
            }
        });
        _chargeTransitions
            .Setup(t => t.ApplyAsync(It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentWebhookInbox, CancellationToken>((webhook, _) => applied = webhook)
            .Returns(Task.CompletedTask);

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeTrue();
        applied.Should().NotBeNull();
        applied!.Intent.Should().Be(WebhookIntent.Authorization);
        applied.NormalizedPayload.PspReference.Should().Be("pi_1");
        applied.NormalizedPayload.Success.Should().BeTrue();
        applied.NormalizedPayload.AmountMinorUnits.Should().Be(2500);
        applied.NormalizedPayload.CurrencyCode.Should().Be("EUR");
        applied.NormalizedPayload.ShopperReference.Should().Be("shopper-1");
        applied.NormalizedPayload.ProviderPayerReference.Should().Be("cus_1");
        applied.NormalizedPayload.StoredPaymentMethodToken.Should().Be("pm_1");
        applied.NormalizedPayload.Brand.Should().Be("visa");
        applied.NormalizedPayload.LastFour.Should().Be("4242");
        _setupTransitions.VerifyNoOtherCalls();
    }

    /// <summary>
    /// The literal incident this feature exists for: a card-setup session Stripe reports complete,
    /// whose <c>setup_intent.succeeded</c> webhook never arrived. Closing the loop needs the
    /// attached payment method, not just the intent's status -- which is exactly what the
    /// <c>expand[]</c> read exists to carry back.
    /// </summary>
    [Fact]
    public async Task A_completed_card_setup_is_applied_through_the_setup_transition_path()
    {
        PaymentWebhookInbox? applied = null;
        GivenPayment(Payment(paymentFlow: PaymentFlows.PaymentMethodSetup));
        GivenProvider(Provider());
        GivenSessionRead(new StripeCheckoutSessionReconciliation
        {
            Id = "cs_test_1",
            Status = "complete",
            PaymentStatus = "no_payment_required",
            SetupIntent = new StripeReconciliationIntent
            {
                Id = "seti_1",
                Status = "succeeded",
                Customer = "cus_1",
                PaymentMethod = new StripeReconciliationPaymentMethod
                {
                    Id = "pm_1",
                    Type = "card",
                    Card = new StripeReconciliationCard { Brand = "visa", Last4 = "4242" }
                }
            }
        });
        _setupTransitions
            .Setup(t => t.ApplyAsync(It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentWebhookInbox, CancellationToken>((webhook, _) => applied = webhook)
            .Returns(Task.CompletedTask);

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeTrue();
        applied.Should().NotBeNull();
        applied!.Intent.Should().Be(WebhookIntent.PaymentMethodSetup);
        applied.NormalizedPayload.PspReference.Should().Be("seti_1");
        applied.NormalizedPayload.Success.Should().BeTrue();
        applied.NormalizedPayload.StoredPaymentMethodToken.Should().Be("pm_1");
        applied.NormalizedPayload.AmountMinorUnits.Should().BeNull(
            "a card setup has no amount, and the transition path never checks one");
        _chargeTransitions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_expired_session_is_applied_as_a_decided_failure()
    {
        PaymentWebhookInbox? applied = null;
        GivenPayment(Payment());
        GivenProvider(Provider());
        GivenSessionRead(new StripeCheckoutSessionReconciliation
        {
            Id = "cs_test_1",
            Status = "expired"
        });
        _chargeTransitions
            .Setup(t => t.ApplyAsync(It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentWebhookInbox, CancellationToken>((webhook, _) => applied = webhook)
            .Returns(Task.CompletedTask);

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeTrue();
        applied!.NormalizedPayload.Success.Should().BeFalse();
        applied.NormalizedPayload.PspReference.Should().Be($"reconciled-expiry:{PaymentId}",
            "no intent was ever created, so the reference must never collide with a real event's own");
    }

    [Fact]
    public async Task A_malformed_synthetic_event_is_reported_but_never_thrown()
    {
        GivenPayment(Payment());
        GivenProvider(Provider());
        GivenSessionRead(new StripeCheckoutSessionReconciliation
        {
            Id = "cs_test_1",
            Status = "complete",
            PaymentStatus = "paid",
            AmountTotal = 2500,
            Currency = "eur",
            PaymentIntent = new StripeReconciliationIntent { Id = "pi_1", Status = "succeeded" }
        });
        _chargeTransitions
            .Setup(t => t.ApplyAsync(It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var reconciled = await Service().TryReconcileAsync(TenantId, PaymentId, CancellationToken.None);

        reconciled.Should().BeFalse();
    }
}
