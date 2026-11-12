using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using XUnitTest.Payment;

namespace XUnitTest.Subscription.Offline;

/// <summary>
/// Renewing a subscription paid by bank transfer under a contract (spec 006).
/// </summary>
/// <remarks>
/// Guards the two ways an offline customer gets hurt: charged by Utilities for money they pay by
/// bank transfer, or locked out (<c>Unpaid</c>) at the first renewal because no card was ever on
/// file. Also guards the invoice: one record per period, never two, with the amount the period was
/// priced at.
/// </remarks>
public sealed class OfflineSubscriptionRenewalTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<IBillingAccountRepository> _billingAccounts = new();
    private readonly Mock<ISubscriptionBillingGateway> _gateway = new(MockBehavior.Strict);
    private readonly Mock<ISubscriptionFinancialDocumentAnnouncer> _documents = new();
    private readonly ControlledTimeProvider _time =
        new(new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero));

    private readonly List<SubscriptionTransition> _transitions = [];

    public OfflineSubscriptionRenewalTests() =>
        _subscriptions
            .Setup(repository => repository.TryTransitionAsync(
                TenantId,
                "sub-1",
                It.IsAny<SubscriptionTransition>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionTransition, CancellationToken>(
                (_, _, transition, _) => _transitions.Add(transition))
            .ReturnsAsync(true);

    [Fact]
    public async Task An_offline_renewal_with_no_card_opens_the_next_period_instead_of_going_unpaid()
    {
        var subscription = OfflineSubscription(SubscriptionStatus.Active);

        await Service().RenewAsync(subscription, CancellationToken.None);

        var transition = _transitions.Should().ContainSingle().Subject;
        transition.NewStatus.Should().Be(
            SubscriptionStatus.Active,
            because: "a contract customer has no card by design, and losing access at the first " +
                "renewal for lacking one is the bug this feature exists to remove");
        transition.CurrentPeriodStartUtc.Should().Be(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        transition.CurrentPeriodEndUtc.Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task An_offline_renewal_never_reads_the_billing_account_or_calls_the_gateway()
    {
        await Service().RenewAsync(OfflineSubscription(SubscriptionStatus.Active), CancellationToken.None);

        // The strict gateway mock throws on any call; this asserts the intent as well.
        _gateway.VerifyNoOtherCalls();
        _billingAccounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_offline_renewal_audits_a_recorded_period_rather_than_a_charge()
    {
        var audit = new Mock<ISubscriptionAuditTrail>();
        var stages = new List<string>();
        audit
            .Setup(trail => trail.RecordAsync(It.IsAny<SubscriptionAuditEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SubscriptionAuditEvent, CancellationToken>((entry, _) => stages.Add(entry.Stage))
            .Returns(Task.CompletedTask);

        await Service(audit.Object).RenewAsync(OfflineSubscription(SubscriptionStatus.Active), CancellationToken.None);

        stages.Should().Contain("RecordedOffline").And.NotContain("ChargeCompleted",
            "nothing reached the gateway, so the trail must not say a charge completed");
    }

    [Fact]
    public async Task A_stored_card_on_the_billing_account_is_never_charged_for_an_offline_subscription()
    {
        _billingAccounts
            .Setup(repository => repository.GetAsync(TenantId, "acct-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                TenantId = TenantId,
                OrganizationId = OrganizationId,
                ProviderName = "STRIPE",
                DefaultPaymentMethodId = "pm-1"
            });

        await Service().RenewAsync(OfflineSubscription(SubscriptionStatus.Active), CancellationToken.None);

        _gateway.Verify(
            gateway => gateway.ChargeAsync(
                It.IsAny<SubscriptionChargeRequest>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "the customer pays this contract by bank transfer; charging the card too bills them twice");
    }

    [Fact]
    public async Task An_offline_renewal_records_its_invoice_in_the_same_write_that_opens_the_period()
    {
        await Service().RenewAsync(OfflineSubscription(SubscriptionStatus.Active), CancellationToken.None);

        var source = _transitions.Single().DocumentSource;

        source.Should().NotBeNull(
            because: "no payment exists for any sweep to rediscover this invoice from, so it is " +
                "recorded atomically with the period or it can be lost");
        source!.DocumentType.Should().Be(FinancialDocumentType.Invoice);
        source.ChargeKind.Should().Be(SubscriptionChargeKind.Renewal);
        source.PaymentDetailId.Should().BeNull(because: "nothing was collected");
        source.SourceKey.Should().Be(FinancialDocumentSourceKey.ForOfflineCharge(
            "sub-1", SubscriptionChargeKind.Renewal, source.PeriodKey));
        source.Amounts!.TotalMinor.Should().Be(
            8_900,
            because: "the record states what the period was priced at, so the customer's own " +
                "invoice can be written from it");
        source.Period.StartUtc.Should().Be(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task An_offline_renewal_asks_for_its_documents_to_be_written_promptly()
    {
        await Service().RenewAsync(OfflineSubscription(SubscriptionStatus.Active), CancellationToken.None);

        _documents.Verify(
            documents => documents.RequestPendingAsync(
                It.Is<SubscriptionDetail>(subscription => subscription.ItemId == "sub-1"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _documents.Verify(
            documents => documents.AnnounceChargeAsync(
                It.IsAny<SubscriptionDetail>(),
                It.IsAny<string>(),
                It.IsAny<SubscriptionChargeKind>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<FinancialDocumentPerson?>()),
            Times.Never,
            "there is no payment to announce, so the payment path must not be asked to find one");
    }

    [Fact]
    public async Task Two_offline_renewals_in_a_row_each_open_their_own_period_and_record_their_own_invoice()
    {
        var subscription = OfflineSubscription(SubscriptionStatus.Active);

        await Service().RenewAsync(subscription, CancellationToken.None);

        subscription.CurrentPeriodStartUtc = _transitions[0].CurrentPeriodStartUtc!.Value;
        subscription.CurrentPeriodEndUtc = _transitions[0].CurrentPeriodEndUtc!.Value;
        _time.Advance(TimeSpan.FromDays(30));

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transitions.Should().HaveCount(2);
        _transitions[1].CurrentPeriodStartUtc.Should().Be(
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            because: "each renewal rolls into the next period on schedule");
        _transitions.Select(transition => transition.DocumentSource!.SourceKey)
            .Should().OnlyHaveUniqueItems(
                because: "two periods are two invoices; a shared key would fold the second into the first");
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_offline_period_that_costs_nothing_records_no_invoice()
    {
        var subscription = OfflineSubscription(SubscriptionStatus.Active);
        subscription.Price.UnitAmountMinor = 0;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transitions.Single().DocumentSource.Should().BeNull(
            because: "a card subscriber is not invoiced for a period that charged nothing, and an " +
                "offline customer is not handed one either");
    }

    [Fact]
    public async Task A_no_price_renewal_records_a_service_statement_even_though_it_costs_nothing()
    {
        var subscription = OfflineSubscription(SubscriptionStatus.Active);
        subscription.OfflinePricing = OfflineSubscriptionTerms.NoPricing();
        OfflineSubscriptionTerms.ApplyNoPrice(subscription.Price, subscription.QuantityItems);

        await Service().RenewAsync(subscription, CancellationToken.None);

        var transition = _transitions.Single();
        transition.NewStatus.Should().Be(SubscriptionStatus.Active);
        transition.DocumentSource!.DocumentType.Should().Be(
            FinancialDocumentType.ServiceStatement,
            because: "the price lives in a contract Utilities never sees, so each renewal is " +
                "recorded as a statement of terms rather than skipped as a zero invoice would be");
        transition.DocumentSource.ChargeKind.Should().Be(SubscriptionChargeKind.Renewal);
        _gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_re_pricing_to_no_price_takes_effect_at_the_renewal_it_was_held_for()
    {
        var subscription = OfflineSubscription(SubscriptionStatus.Active);
        var price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8_900 };
        OfflineSubscriptionTerms.ApplyNoPrice(price, []);
        subscription.PendingPlanChange = new PendingPlanChange
        {
            Plan = subscription.Plan,
            Price = price,
            QuantityItems = [],
            FeeSchedule = subscription.FeeSchedule,
            UsageSchedule = subscription.FeeSchedule,
            EffectiveAtUtc = subscription.CurrentPeriodEndUtc,
            OfflinePricing = OfflineSubscriptionTerms.NoPricing()
        };

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transitions.Single().DocumentSource!.DocumentType.Should().Be(
            FinancialDocumentType.ServiceStatement,
            because: "the period the new pricing opens is the first one it governs, so its record " +
                "must already be a statement, not a zero invoice that consumes an invoice number");
    }

    [Fact]
    public async Task An_offline_trial_converts_to_active_with_a_record_invoice_and_never_to_unpaid()
    {
        var subscription = OfflineSubscription(SubscriptionStatus.Trialing);
        subscription.Trial = new TrialTerms
        {
            StartsAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            RequiresPaymentMethod = false
        };
        _time.Advance(TimeSpan.FromHours(2));

        await Service().RenewAsync(subscription, CancellationToken.None);

        var transition = _transitions.Single();
        transition.NewStatus.Should().Be(
            SubscriptionStatus.Active,
            because: "a card-free trial with no card goes Unpaid on the card path; an offline one " +
                "converts, because its money comes by bank transfer");
        transition.DocumentSource.Should().NotBeNull();
    }

    [Fact]
    public async Task A_re_pricing_held_for_this_renewal_is_installed_and_invoiced_at_its_new_figure()
    {
        var subscription = OfflineSubscription(SubscriptionStatus.Active);
        subscription.PendingPlanChange = new PendingPlanChange
        {
            Plan = subscription.Plan,
            Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 12_000 },
            QuantityItems = [],
            FeeSchedule = subscription.FeeSchedule,
            UsageSchedule = subscription.FeeSchedule,
            EffectiveAtUtc = subscription.CurrentPeriodEndUtc,
            OfflinePricing = new OfflinePricing
            {
                Mode = OfflinePricingMode.Contract,
                ContractAmountMinor = 12_000,
                ContractCurrencyCode = "CHF"
            }
        };

        await Service().RenewAsync(subscription, CancellationToken.None);

        var transition = _transitions.Single();
        transition.OfflinePricing!.ContractAmountMinor.Should().Be(
            12_000,
            because: "the pricing record is installed in the same write as the price it describes, " +
                "so the two can never disagree");
        transition.DocumentSource!.Amounts!.TotalMinor.Should().Be(
            12_000,
            because: "the first period after a re-pricing is invoiced at the new figure");
    }

    [Fact]
    public async Task A_card_subscription_without_a_card_still_goes_unpaid()
    {
        var subscription = OfflineSubscription(SubscriptionStatus.Active);
        subscription.CollectionMethod = SubscriptionCollectionMethod.AutomaticCharge;

        await Service().RenewAsync(subscription, CancellationToken.None);

        _transitions.Single().NewStatus.Should().Be(
            SubscriptionStatus.Unpaid,
            because: "the offline branch must not leak into card subscriptions, which would grant " +
                "every card-less subscriber free renewals");
    }

    private SubscriptionRenewalService Service(ISubscriptionAuditTrail? audit = null) => new(
        _subscriptions.Object,
        _billingAccounts.Object,
        _gateway.Object,
        new SubscriptionOutboxEventFactory(),
        Mock.Of<IEntitlementSnapshotCache>(),
        new OptionsStub(),
        NullLogger<SubscriptionRenewalService>.Instance,
        _time,
        audit: audit,
        documents: _documents.Object);

    private static SubscriptionDetail OfflineSubscription(SubscriptionStatus status) => new()
    {
        ItemId = "sub-1",
        TenantId = TenantId,
        OrganizationId = OrganizationId,
        BillingAccountId = "acct-1",
        Status = status,
        CollectionMethod = SubscriptionCollectionMethod.OfflineInvoice,
        OfflinePricing = new OfflinePricing { Mode = OfflinePricingMode.Catalogue },
        CurrencyCode = "CHF",
        CurrentPeriodStartUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentPeriodEndUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        Plan = new PlanSnapshot { Code = "professional", DisplayName = "Professional" },
        Price = new PriceSnapshot { CurrencyCode = "CHF", UnitAmountMinor = 8_900 },
        FeeSchedule = new BillingSchedule
        {
            Interval = BillingInterval.Month,
            IntervalCount = 1,
            AnchorInstantUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TimeZoneId = "UTC",
            AnchorDayOfMonth = 1
        }
    };

    private sealed class OptionsStub : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue { get; } = new() { DunningMaxAttempts = 4 };

        public SubscriptionOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
