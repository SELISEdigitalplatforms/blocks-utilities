using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Models.Webhooks;
using Payment.DomainService.Outbox;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Collecting a card without charging it: the request that asks for it, the event that reports
/// it, and the record it leaves behind.
/// </summary>
/// <remarks>
/// The recurring hazard throughout is that a setup record looks enough like a payment to be
/// mistaken for one — it settles at Authorized, it carries a provider reference, it lives in the
/// payments collection. Several of these tests exist only to prove that the things which count,
/// refund or capture money decline to touch it.
/// </remarks>
public sealed class PaymentMethodSetupTests
{
    private const string TenantId = "tenant-1";

    private readonly StripeSetupSessionRequestFactory _factory = new();
    private readonly StripeWebhookNormalizer _normalizer = new();

    [Fact]
    public void The_factory_serves_stripe_alone()
    {
        _factory.Supports(PaymentConstants.StripeProvider).Should().BeTrue();
        _factory.Supports(PaymentConstants.AdyenOnlineProvider).Should().BeFalse();
    }

    /// <summary>
    /// Setup mode, and nothing that would move money. A line item or an amount here is the
    /// difference between storing a card and charging one.
    /// </summary>
    [Fact]
    public void The_session_collects_a_card_and_charges_nothing()
    {
        var request = Create();
        var form = ReadForm(request);

        form["mode"].Should().Be("setup");
        form["currency"].Should().Be("chf",
            "Stripe decides which methods to offer from the currency, and there is no amount " +
            "for it to infer one from");
        form.Keys.Should().NotContain(key => key.StartsWith("line_items", StringComparison.Ordinal));
        form.Keys.Should().NotContain("amount");
        form.Keys.Should().NotContain("payment_intent_data[setup_future_usage]");
        request.AmountMinorUnits.Should().Be(0);
    }

    /// <summary>
    /// Session metadata does not reach the SetupIntent, and setup_intent.succeeded is raised
    /// against the intent. Without this copy the one event that reports the stored card arrives
    /// with nothing to route it home.
    /// </summary>
    [Fact]
    public void Routing_metadata_is_copied_onto_the_setup_intent()
    {
        var form = ReadForm(Create());

        form["metadata[tenant_reference]"].Should().Be("p1.tenant.payment");
        form["setup_intent_data[metadata][tenant_reference]"].Should().Be("p1.tenant.payment");
        form[$"setup_intent_data[metadata][{StripeRoutingMetadata.ShopperReferenceKey}]"]
            .Should().Be("shopper-1");
        form[$"setup_intent_data[metadata][{StripeRoutingMetadata.OrganizationKey}]"]
            .Should().Be("org-1");
    }

    /// <summary>
    /// Naming a customer Stripe already knows is what keeps a returning shopper from becoming a
    /// second customer with the card saved somewhere nothing will look.
    /// </summary>
    [Fact]
    public void A_known_shopper_is_named_rather_than_created_again()
    {
        ReadForm(Create(providerPayerReference: "cus_1"))["customer"].Should().Be("cus_1");
        ReadForm(Create()).Keys.Should().NotContain("customer");
    }

    [Fact]
    public void A_succeeded_setup_intent_reports_the_card_it_stored()
    {
        var parsed = Parse(
            """
            {"id":"evt_1","type":"setup_intent.succeeded","created":1780000000,
             "data":{"object":{"id":"seti_1","customer":"cus_1","payment_method":"pm_1",
             "metadata":{"tenant_reference":"p1.tenant.payment","payment_id":"payment-1",
             "shopper_reference":"shopper-1"}}}}
            """);

        parsed.Intent.Should().Be(WebhookIntent.PaymentMethodSetup,
            "an authorisation is proved against the payment's amount, and there is none");
        parsed.Payload.Success.Should().BeTrue();
        parsed.Payload.StoredPaymentMethodToken.Should().Be("pm_1");
        parsed.Payload.ProviderPayerReference.Should().Be("cus_1");
        parsed.RoutingReference.Should().Be("p1.tenant.payment");
    }

    [Fact]
    public void A_failed_setup_intent_reports_why_under_its_own_error_field()
    {
        var parsed = Parse(
            """
            {"id":"evt_2","type":"setup_intent.setup_failed","created":1780000000,
             "data":{"object":{"id":"seti_2","last_setup_error":{"code":"card_declined"},
             "metadata":{"tenant_reference":"p1.tenant.payment"}}}}
            """);

        parsed.Intent.Should().Be(WebhookIntent.PaymentMethodSetup);
        parsed.Payload.Success.Should().BeFalse();
        parsed.Payload.ProviderFailureCode.Should().Be("card_declined");
    }

    [Fact]
    public async Task A_stored_card_settles_the_setup_and_is_recorded()
    {
        var harness = new TransitionHarness(SetupRecord());

        await harness.ApplyAsync(SetupEvent(succeeded: true));

        harness.Authorised.Should().BeTrue();
        harness.AuthorisedAmount.Should().Be(0m);
        harness.CapturedAutomatically.Should().BeFalse(
            "there is nothing to capture, and a captured record is one every financial total " +
            "would pick up");
        harness.StoredCard.Should().BeTrue();
        harness.Operations.Should().Equal(["store-card", "publish-confirmation"],
            "activation can observe the payment outbox as soon as confirmation is written");
    }

    [Fact]
    public async Task A_failed_setup_settles_without_recording_a_card()
    {
        var harness = new TransitionHarness(SetupRecord());

        await harness.ApplyAsync(SetupEvent(succeeded: false));

        harness.Authorised.Should().BeFalse();
        harness.StoredCard.Should().BeFalse();
    }

    /// <summary>
    /// A session expires after it has been used, or the events arrive out of order. Either way
    /// the card is stored and the subscription may already be running.
    /// </summary>
    [Fact]
    public async Task An_expiry_that_arrives_after_the_card_was_stored_changes_nothing()
    {
        var settled = SetupRecord();
        settled.PaymentStatus = PaymentStatuses.Authorized;
        settled.WebhookConfirmedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        var harness = new TransitionHarness(settled);

        await harness.ApplyAsync(ExpiredSessionEvent());

        harness.Applied.Should().BeFalse();
    }

    /// <summary>
    /// One provider event name covers every kind of session ending. An abandoned *checkout* has
    /// always been a no-op, and that is not a decision for a card-setup handler to reverse.
    /// </summary>
    [Fact]
    public async Task An_expiring_payment_session_is_left_alone()
    {
        var checkout = SetupRecord();
        checkout.PaymentFlow = PaymentFlows.HostedCheckout;
        var harness = new TransitionHarness(checkout);

        await harness.ApplyAsync(ExpiredSessionEvent());

        harness.Applied.Should().BeFalse();
        harness.StoredCard.Should().BeFalse();
    }

    private static PaymentWebhookInbox SetupEvent(bool succeeded) => new()
    {
        TenantId = TenantId,
        WebhookId = Guid.NewGuid().ToString(),
        Intent = WebhookIntent.PaymentMethodSetup,
        EventCode = succeeded ? "setup_intent.succeeded" : "setup_intent.setup_failed",
        EventDateUtc = DateTime.UtcNow,
        NormalizedPayload = new PaymentWebhookPayload
        {
            PaymentDetailId = "payment-1",
            PspReference = "seti_1",
            Success = succeeded,
            ProviderName = PaymentConstants.StripeProvider,
            ShopperReference = "shopper-1",
            StoredPaymentMethodToken = succeeded ? "pm_1" : null
        }
    };

    /// <summary>
    /// A successful authorisation the documented, separately-delivered way: no token or shopper
    /// reference inline, because those arrive on their own recurring.token.created webhook.
    /// </summary>
    private static PaymentWebhookInbox SetupEventWithoutInlineToken() => new()
    {
        TenantId = TenantId,
        WebhookId = Guid.NewGuid().ToString(),
        Intent = WebhookIntent.PaymentMethodSetup,
        EventCode = "setup_intent.succeeded",
        EventDateUtc = DateTime.UtcNow,
        NormalizedPayload = new PaymentWebhookPayload
        {
            PaymentDetailId = "payment-1",
            PspReference = "seti_1",
            Success = true,
            ProviderName = PaymentConstants.StripeProvider
        }
    };

    private static PaymentWebhookInbox ExpiredSessionEvent() => new()
    {
        TenantId = TenantId,
        WebhookId = Guid.NewGuid().ToString(),
        Intent = WebhookIntent.Cancelled,
        EventCode = "checkout.session.expired",
        EventDateUtc = DateTime.UtcNow,
        NormalizedPayload = new PaymentWebhookPayload
        {
            PaymentDetailId = "payment-1",
            PspReference = "cs_1",
            Success = false
        }
    };

    private static PaymentDetail SetupRecord() => new()
    {
        ItemId = "payment-1",
        TenantId = TenantId,
        PaymentFlow = PaymentFlows.PaymentMethodSetup,
        PaymentStatus = PaymentStatuses.Processing,
        CurrencyCode = "CHF",
        PreciseAmount = 0,
        RememberCard = true,
        ShopperReference = "shopper-1",
        OrganizationId = "org-1"
    };

    private ProviderInitiationRequest Create(string? providerPayerReference = null) =>
        _factory.Create(
            new CreatePaymentMethodSetupRequest
            {
                ProviderName = PaymentConstants.StripeProvider,
                CurrencyCode = "CHF",
                OrderId = "sub:subscription-1",
                Description = "Community subscription",
                CustomerOrganizationId = "subscriber-1"
            },
            new PaymentDetail
            {
                ItemId = "payment-1",
                TenantId = TenantId,
                CurrencyCode = "CHF",
                OrganizationId = "org-1"
            },
            new PaymentProvider
            {
                ProviderName = PaymentConstants.StripeProvider,
                MerchantId = "acct_1",
                ApiBaseUrl = StripeConstants.ApiBaseUrl
            },
            "https://app.example.com/return",
            "p1.tenant.payment",
            "shopper-1",
            providerPayerReference);

    private static Dictionary<string, string> ReadForm(ProviderInitiationRequest request) =>
        request.Payload.Elements.ToDictionary(
            element => element.Name,
            element => element.Value.AsString,
            StringComparer.Ordinal);

    private ParsedWebhookEvent Parse(string body)
    {
        var result = _normalizer.Parse(
            body,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [StripeConstants.SignatureHeader] = "t=1,v1=abc"
            });

        result.Events.Should().ContainSingle();

        return result.Events[0];
    }

    /// <summary>
    /// The transition service with its two collaborators watched: what it wrote to the payment,
    /// and whether it went on to record the card.
    /// </summary>
    /// <remarks>
    /// The underlying <paramref name="payment"/> object doubles as this harness's tiny fake
    /// store: <c>TryRecordSetup*ConfirmedAsync</c> mutate it directly (first write wins, exactly
    /// like the real repository's null-filtered update), and <c>GetByIdAsync</c> always reads it
    /// back live rather than a frozen snapshot -- which is what lets
    /// <c>TryCompleteIfReadyAsync</c>'s re-read see a signal recorded by an earlier call in the
    /// same test. <c>ApplyAuthorisationAsync</c> mirrors the real repository's own deduplication
    /// -- a repeat call with a dedup key already applied is a no-op -- so a test can assert a
    /// duplicate delivery does not double-publish.
    /// </remarks>
    private sealed class TransitionHarness
    {
        private readonly PaymentDetail _payment;
        private readonly Mock<IPaymentRepository> _payments = new();
        private readonly Mock<IStoredPaymentMethodLifecycleService> _storedMethods = new();
        private readonly HashSet<string> _appliedDeduplicationKeys = [];

        public TransitionHarness(PaymentDetail payment)
        {
            _payment = payment;

            _payments
                .Setup(repository => repository.GetByIdAsync(
                    TenantId, payment.ItemId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _payment);

            _payments
                .Setup(repository => repository.TryRecordSetupAuthorizationConfirmedAsync(
                    TenantId, payment.ItemId, It.IsAny<DateTime>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback((string _, string _, DateTime eventDateUtc, string pspReference, CancellationToken _) =>
                {
                    if (_payment.SetupAuthorizationConfirmedAtUtc is null)
                    {
                        _payment.SetupAuthorizationConfirmedAtUtc = eventDateUtc;
                        _payment.PspReference = pspReference;
                    }
                })
                .ReturnsAsync(() => _payment.SetupAuthorizationConfirmedAtUtc is not null);

            _payments
                .Setup(repository => repository.TryRecordSetupTokenConfirmedAsync(
                    TenantId, payment.ItemId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string _, DateTime eventDateUtc, CancellationToken _) =>
                {
                    _payment.SetupTokenConfirmedAtUtc ??= eventDateUtc;
                })
                .ReturnsAsync(() => _payment.SetupTokenConfirmedAtUtc is not null);

            _payments
                .Setup(repository => repository.ApplyAuthorisationAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<decimal>(),
                    It.IsAny<bool>(),
                    It.IsAny<string>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<PaymentInstrument?>(),
                    It.IsAny<PaymentOutboxEvent>(),
                    It.IsAny<CancellationToken>()))
                .Returns(
                    (
                        string _,
                        string _,
                        bool authorized,
                        decimal amount,
                        bool captured,
                        string _,
                        DateTime _,
                        PaymentInstrument? _,
                        PaymentOutboxEvent outboxEvent,
                        CancellationToken _) =>
                    {
                        // The real repository refuses to re-apply an event whose deduplication
                        // key it has already seen, which is exactly what makes a duplicate
                        // delivery -- or two calls converging on the same "setup-ready" key from
                        // either signal's own handler -- a safe no-op rather than a double
                        // completion.
                        if (!_appliedDeduplicationKeys.Add(outboxEvent.DeduplicationKey))
                        {
                            return Task.FromResult(false);
                        }

                        Operations.Add("publish-confirmation");
                        Applied = true;
                        Authorised = authorized;
                        AuthorisedAmount = amount;
                        CapturedAutomatically = captured;
                        return Task.FromResult(true);
                    });

            _storedMethods
                .Setup(service => service.ApplyAuthorisationTokenAsync(
                    It.IsAny<PaymentWebhookInbox>(),
                    It.IsAny<PaymentDetail>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    StoredCard = true;
                    Operations.Add("store-card");
                })
                .Returns(Task.CompletedTask);
        }

        public bool Applied { get; private set; }

        public bool Authorised { get; private set; }

        public decimal AuthorisedAmount { get; private set; }

        public bool CapturedAutomatically { get; private set; }

        public bool StoredCard { get; private set; }

        public List<string> Operations { get; } = [];

        public Task ApplyAsync(PaymentWebhookInbox webhook) =>
            new PaymentMethodSetupWebhookStateTransitionService(
                _payments.Object,
                _storedMethods.Object,
                new PaymentOutboxEventFactory(),
                NullLogger<PaymentMethodSetupWebhookStateTransitionService>.Instance)
                .ApplyAsync(webhook, CancellationToken.None);
    }

    /// <summary>
    /// Finding 3: a setup only becomes Ready once both the authorization and the token signals
    /// are on the record, in whichever order they arrived -- never inferred from one event's
    /// silence about the other signal.
    /// </summary>
    [Fact]
    public async Task A_successful_authorisation_with_no_inline_token_waits_for_the_token_signal()
    {
        var harness = new TransitionHarness(SetupRecord());

        // No StoredPaymentMethodToken/ShopperReference on this event -- the documented shape for
        // a token that arrives on its own, separate recurring.token.created webhook rather than
        // inline, per https://docs.adyen.com/online-payments/tokenization/create-tokens.
        await harness.ApplyAsync(SetupEventWithoutInlineToken());

        harness.Applied.Should().BeFalse(
            "a successful authorisation alone is only one of the two signals a setup needs");
        harness.StoredCard.Should().BeFalse();
        harness.Operations.Should().BeEmpty();
    }

    [Fact]
    public async Task The_token_signal_arriving_first_lets_a_later_authorisation_complete_the_setup()
    {
        var payment = SetupRecord();
        var harness = new TransitionHarness(payment);

        // Simulates the token webhook (handled separately by
        // StoredPaymentMethodLifecycleService.ApplyTokenEventAsync) having already recorded its
        // signal before this authorisation webhook is processed.
        payment.SetupTokenConfirmedAtUtc = DateTime.UtcNow.AddSeconds(-1);

        // No inline token on this event -- the authorisation signal alone must still find the
        // already-recorded token signal and complete the setup.
        await harness.ApplyAsync(SetupEventWithoutInlineToken());

        harness.Applied.Should().BeTrue();
        harness.Authorised.Should().BeTrue();
    }

    [Fact]
    public async Task A_duplicate_authorisation_delivery_does_not_double_complete()
    {
        var harness = new TransitionHarness(SetupRecord());
        var succeededEvent = SetupEvent(succeeded: true);

        await harness.ApplyAsync(succeededEvent);
        await harness.ApplyAsync(succeededEvent);

        // Storing the card again on a repeat delivery is harmless -- UpsertFromProviderAsync is
        // itself idempotent on the token fingerprint -- so only the completion itself, which
        // flips status and publishes the outbox event exactly once, is asserted here.
        harness.Operations.Count(operation => operation == "publish-confirmation").Should().Be(
            1,
            "the second, identical delivery must be a no-op rather than a second completion");
    }
}
