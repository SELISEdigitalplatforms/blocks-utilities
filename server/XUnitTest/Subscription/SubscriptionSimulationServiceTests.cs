using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Outbox;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;
using Subscription.DomainService.Simulation;

namespace XUnitTest.Subscription;

/// <summary>
/// <see cref="SubscriptionSimulationService"/> in isolation, every collaborator mocked.
/// </summary>
/// <remarks>
/// <see cref="BlocksContext"/> is ambient (an <c>AsyncLocal</c>), so every test sets and clears
/// it itself rather than relying on ordering between tests — xUnit does not guarantee one test's
/// context is gone before the next starts on the same thread.
/// </remarks>
public sealed class SubscriptionSimulationServiceTests : IDisposable
{
    private const string ConsoleOrganizationId = "console-org";
    private const string SubscriptionId = "sub-1";
    private const string TenantId = "tenant-1";
    private const string CorrelationId = "corr-1";

    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionResponseMapper> _responseMapper = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<ISubscriptionInvoiceHistoryRepository> _invoiceHistory = new();
    private readonly Mock<ISubscriptionUsageInvoiceRepository> _usageInvoices = new();
    private readonly Mock<ISubscriptionPaymentLinkRepository> _paymentLinks = new();
    private readonly Mock<ISubscriptionAuditRepository> _auditEvents = new();
    private readonly Mock<ISubscriptionSimulationRunRepository> _simulationRuns = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _paymentOptions = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<ICurrencyMinorUnitResolver> _minorUnits = new();
    private readonly Mock<IPaymentWebhookStateTransitionService> _webhookTransitions = new();
    private readonly Mock<ISubscriptionActivationProcessor> _activationProcessor = new();
    private readonly Mock<ISubscriptionRenewalService> _renewalService = new();
    private readonly Mock<ISubscriptionSimulatedOutcomeSource> _scriptedOutcomes = new();
    private readonly Mock<ISubscriptionUsageRatingProcessor> _usageRatingProcessor = new();
    private readonly Mock<ISubscriptionOutboxProcessor> _outboxProcessor = new();

    public SubscriptionSimulationServiceTests()
    {
        _paymentOptions
            .Setup(options => options.CurrentValue)
            .Returns(new PaymentOptions { ConsoleOrganizationId = ConsoleOrganizationId });
        _minorUnits
            .Setup(resolver => resolver.TryConvert(It.IsAny<decimal>(), It.IsAny<string>(), out It.Ref<long>.IsAny))
            .Returns(new TryConvertCallback((decimal amount, string _, out long minorUnits) =>
            {
                minorUnits = (long)(amount * 100);
                return true;
            }));
    }

    private delegate bool TryConvertCallback(decimal amount, string currencyCode, out long minorUnits);

    public void Dispose() => BlocksContext.ClearContext();

    [Fact]
    public async Task Refuses_a_caller_who_is_not_the_console()
    {
        SetCaller(organizationId: "some-other-org", permissions: [SubscriptionSimulationGuard.SimulationAdministratorPermission]);

        var result = await GetStateAsync(organizationId: "target-org");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_forbidden");
        _contextResolver.Verify(
            resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an unauthorized caller must never reach a repository round trip");
    }

    [Fact]
    public async Task Refuses_the_console_without_the_simulation_permission()
    {
        SetCaller(organizationId: ConsoleOrganizationId, permissions: []);

        var result = await GetStateAsync(organizationId: "target-org");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_forbidden");
    }

    [Fact]
    public async Task Requires_an_organization_because_the_console_has_none_of_its_own()
    {
        SetAuthorizedCaller();

        var result = await GetStateAsync(organizationId: null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_organization_required");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public async Task Rejects_an_out_of_range_limit(int limit)
    {
        SetAuthorizedCaller();

        var result = await GetStateAsync(organizationId: "target-org", auditLimit: limit);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_limit_invalid");
    }

    [Fact]
    public async Task Reports_not_found_rather_than_leaking_that_the_subscription_belongs_elsewhere()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDetail?)null);

        var result = await GetStateAsync(organizationId: "target-org");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_not_found");
        _simulationRuns.Verify(
            runs => runs.AppendAsync(
                It.Is<SubscriptionSimulationRun>(run => run.Outcome == "Failed"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Assembles_the_state_from_every_collaborator_on_the_happy_path()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");

        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId,
            TenantId = TenantId,
            OrganizationId = "target-org",
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _responseMapper
            .Setup(mapper => mapper.ToResponse(subscription, null))
            .Returns(new SubscriptionResponse { SubscriptionId = SubscriptionId });
        _entitlements
            .Setup(service => service.GetAsync(true, "target-org", CorrelationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<EntitlementSnapshotResponse>.Success(
                new EntitlementSnapshotResponse(), CorrelationId));
        _invoiceHistory
            .Setup(repo => repo.ListBySubscriptionAsync(
                TenantId, "target-org", SubscriptionId, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SubscriptionInvoiceHistoryRecord>)[]);
        _usageInvoices
            .Setup(repo => repo.ListBySubscriptionAsync(TenantId, SubscriptionId, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SubscriptionUsageInvoice>)[]);
        _paymentLinks
            .Setup(repo => repo.FindBySubscriptionAsync(TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionPaymentLink?)null);
        _auditEvents
            .Setup(repo => repo.ListAsync(TenantId, "target-org", SubscriptionId, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SubscriptionAuditEvent>)[]);

        var result = await GetStateAsync(organizationId: "target-org");

        result.IsSuccess.Should().BeTrue();
        result.Value!.SubscriptionId.Should().Be(SubscriptionId);
        result.Value!.TenantId.Should().Be(TenantId);
        result.Value!.OrganizationId.Should().Be("target-org");
        result.Value!.Entitlements.Should().NotBeNull();
        _simulationRuns.Verify(
            runs => runs.AppendAsync(
                It.Is<SubscriptionSimulationRun>(run => run.Outcome == "Succeeded"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void SetAuthorizedCaller() =>
        SetCaller(ConsoleOrganizationId, [SubscriptionSimulationGuard.SimulationAdministratorPermission]);

    private static void SetCaller(string? organizationId, IEnumerable<string> permissions) =>
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: TenantId,
            roles: [],
            userId: "user-1",
            isAuthenticated: true,
            requestUri: null,
            organizationId: organizationId,
            expireOn: DateTime.UtcNow.AddHours(1),
            email: "tester@example.com",
            permissions: permissions,
            userName: null,
            phoneNumber: null,
            displayName: null,
            oauthToken: null,
            originalTenantId: null));

    private void ResolvesContext(string organizationId) =>
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(CorrelationId, organizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, organizationId, "user-1", "user-1")));

    private Task<SubscriptionOperationResult<SubscriptionSimulationStateResponse>> GetStateAsync(
        string? organizationId,
        int auditLimit = 100,
        int paymentLimit = 100) =>
        CreateService().GetStateAsync(
            SubscriptionId, organizationId, auditLimit, paymentLimit,
            includeBackgroundWork: false, CorrelationId, CancellationToken.None);

    private SubscriptionSimulationService CreateService() => new(
        _contextResolver.Object,
        _subscriptions.Object,
        _responseMapper.Object,
        _entitlements.Object,
        _invoiceHistory.Object,
        _usageInvoices.Object,
        _paymentLinks.Object,
        _auditEvents.Object,
        _simulationRuns.Object,
        _paymentOptions.Object,
        _payments.Object,
        _minorUnits.Object,
        _webhookTransitions.Object,
        _activationProcessor.Object,
        _renewalService.Object,
        _scriptedOutcomes.Object,
        _usageRatingProcessor.Object,
        _outboxProcessor.Object);

    /// <summary>Wires the read-only GetStateAsync collaborators to return an empty-but-valid state, so mark-payment tests can reuse it without re-asserting PR 1's own coverage.</summary>
    private void StubEmptyState(SubscriptionDetail subscription, string organizationId)
    {
        _responseMapper
            .Setup(mapper => mapper.ToResponse(subscription, null))
            .Returns(new SubscriptionResponse { SubscriptionId = subscription.ItemId });
        _entitlements
            .Setup(service => service.GetAsync(true, organizationId, CorrelationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionOperationResult<EntitlementSnapshotResponse>.Success(
                new EntitlementSnapshotResponse(), CorrelationId));
        _invoiceHistory
            .Setup(repo => repo.ListBySubscriptionAsync(
                TenantId, organizationId, subscription.ItemId, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SubscriptionInvoiceHistoryRecord>)[]);
        _usageInvoices
            .Setup(repo => repo.ListBySubscriptionAsync(TenantId, subscription.ItemId, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SubscriptionUsageInvoice>)[]);
        _auditEvents
            .Setup(repo => repo.ListAsync(TenantId, organizationId, subscription.ItemId, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SubscriptionAuditEvent>)[]);
    }

    private static PaymentDetail Payment(string id, decimal amount = 10m, string currency = "EUR") => new()
    {
        ItemId = id,
        PreciseAmount = amount,
        CurrencyCode = currency,
        ProviderName = "stripe",
    };

    private static SubscriptionPaymentLink PendingInitialChargeLink(string paymentId) => new()
    {
        ItemId = "link-1",
        TenantId = TenantId,
        OrganizationId = "target-org",
        SubscriptionId = SubscriptionId,
        PaymentDetailId = paymentId,
        Purpose = SubscriptionPaymentPurpose.InitialCharge,
        State = SubscriptionPaymentLinkState.Pending,
    };

    [Fact]
    public async Task Marking_an_initial_charge_succeeded_settles_the_webhook_and_runs_activation()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Incomplete,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        var link = PendingInitialChargeLink("pay-1");
        _paymentLinks
            .Setup(repo => repo.FindBySubscriptionAsync(TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);
        _payments
            .Setup(repo => repo.GetByIdAsync(TenantId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Payment("pay-1"));
        _activationProcessor
            .Setup(processor => processor.SettleLinkAsync(link, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        StubEmptyState(subscription, "target-org");

        var request = new MarkPaymentSucceededRequest
        {
            OrganizationId = "target-org",
            PaymentPurpose = SubscriptionPaymentPurpose.InitialCharge,
        };
        var result = await CreateService().MarkPaymentSucceededAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be("MarkPaymentSucceeded");
        _webhookTransitions.Verify(
            transitions => transitions.ApplyAsync(
                It.Is<PaymentWebhookInbox>(webhook =>
                    webhook.NormalizedPayload.PaymentDetailId == "pay-1" &&
                    webhook.NormalizedPayload.Success == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _activationProcessor.Verify(
            processor => processor.SettleLinkAsync(link, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Marking_an_initial_charge_failed_settles_a_refusal()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Incomplete,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        var link = PendingInitialChargeLink("pay-1");
        _paymentLinks
            .Setup(repo => repo.FindBySubscriptionAsync(TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);
        _payments
            .Setup(repo => repo.GetByIdAsync(TenantId, "pay-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Payment("pay-1"));
        StubEmptyState(subscription, "target-org");

        var request = new MarkPaymentFailedRequest
        {
            OrganizationId = "target-org",
            PaymentPurpose = SubscriptionPaymentPurpose.InitialCharge,
            FailureKind = SimulatedPaymentFailureKind.Declined,
        };
        var result = await CreateService().MarkPaymentFailedAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _webhookTransitions.Verify(
            transitions => transitions.ApplyAsync(
                It.Is<PaymentWebhookInbox>(webhook => webhook.NormalizedPayload.Success == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _activationProcessor.Verify(
            processor => processor.SettleLinkAsync(link, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(SimulatedPaymentFailureKind.ProviderUnavailable)]
    [InlineData(SimulatedPaymentFailureKind.OutcomeUnknown)]
    public async Task An_ambiguous_initial_charge_outcome_settles_nothing(SimulatedPaymentFailureKind kind)
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Incomplete,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _paymentLinks
            .Setup(repo => repo.FindBySubscriptionAsync(TenantId, SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PendingInitialChargeLink("pay-1"));
        StubEmptyState(subscription, "target-org");

        var request = new MarkPaymentFailedRequest
        {
            OrganizationId = "target-org",
            PaymentPurpose = SubscriptionPaymentPurpose.InitialCharge,
            FailureKind = kind,
        };
        var result = await CreateService().MarkPaymentFailedAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(
            "an unanswered provider leaves the charge exactly where a real one would, which is not a failure of the simulation call itself");
        _webhookTransitions.Verify(
            transitions => transitions.ApplyAsync(It.IsAny<PaymentWebhookInbox>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _activationProcessor.Verify(
            processor => processor.SettleLinkAsync(It.IsAny<SubscriptionPaymentLink>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Refuses_to_settle_an_initial_charge_that_already_settled()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Active,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var request = new MarkPaymentSucceededRequest
        {
            OrganizationId = "target-org",
            PaymentPurpose = SubscriptionPaymentPurpose.InitialCharge,
        };
        var result = await CreateService().MarkPaymentSucceededAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_already_settled");
    }

    [Fact]
    public async Task Marking_a_renewal_succeeded_scripts_the_gateway_and_runs_the_renewal_service()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Active,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        StubEmptyState(subscription, "target-org");

        var request = new MarkPaymentSucceededRequest
        {
            OrganizationId = "target-org",
            PaymentPurpose = SubscriptionPaymentPurpose.Renewal,
        };
        var result = await CreateService().MarkPaymentSucceededAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scriptedOutcomes.Verify(
            source => source.ScriptNext(
                It.Is<ScriptedChargeOutcome>(outcome => outcome.Outcome == SimulatedChargeOutcome.Succeeded)),
            Times.Once);
        _renewalService.Verify(
            service => service.RenewAsync(subscription, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refuses_to_renew_an_incomplete_subscription()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Incomplete,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var request = new MarkPaymentFailedRequest
        {
            OrganizationId = "target-org",
            PaymentPurpose = SubscriptionPaymentPurpose.Renewal,
            FailureKind = SimulatedPaymentFailureKind.Declined,
        };
        var result = await CreateService().MarkPaymentFailedAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_not_renewable");
        _renewalService.Verify(
            service => service.RenewAsync(It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Defers_a_renewal_while_a_settlement_reservation_is_unresolved()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Active,
            SettlementReservation = new SettlementReservation { ReservationId = "res-1" },
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var request = new MarkPaymentSucceededRequest
        {
            OrganizationId = "target-org",
            PaymentPurpose = SubscriptionPaymentPurpose.Renewal,
        };
        var result = await CreateService().MarkPaymentSucceededAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_settlement_in_flight");
    }

    [Fact]
    public async Task Advancing_a_renewal_scripts_the_gateway_and_runs_the_real_renewal_service()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Active,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        StubEmptyState(subscription, "target-org");

        var request = new AdvanceRenewalRequest
        {
            OrganizationId = "target-org",
            PaymentOutcome = SimulatedRenewalOutcome.Succeeded,
        };
        var result = await CreateService().AdvanceRenewalAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Action.Should().Be("AdvanceRenewal");
        _scriptedOutcomes.Verify(
            source => source.ScriptNext(
                It.Is<ScriptedChargeOutcome>(outcome => outcome.Outcome == SimulatedChargeOutcome.Succeeded)),
            Times.Once);
        _renewalService.Verify(
            service => service.RenewAsync(subscription, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(SimulatedRenewalOutcome.Declined, SimulatedChargeOutcome.Rejected)]
    [InlineData(SimulatedRenewalOutcome.InsufficientFunds, SimulatedChargeOutcome.Rejected)]
    [InlineData(SimulatedRenewalOutcome.PaymentMethodExpired, SimulatedChargeOutcome.Rejected)]
    [InlineData(SimulatedRenewalOutcome.ProviderUnavailable, SimulatedChargeOutcome.Unavailable)]
    [InlineData(SimulatedRenewalOutcome.OutcomeUnknown, SimulatedChargeOutcome.TimedOut)]
    public async Task Advancing_a_renewal_maps_every_outcome_onto_the_gateway_script(
        SimulatedRenewalOutcome requested, SimulatedChargeOutcome expected)
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.PastDue,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        StubEmptyState(subscription, "target-org");

        var request = new AdvanceRenewalRequest { OrganizationId = "target-org", PaymentOutcome = requested };
        var result = await CreateService().AdvanceRenewalAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scriptedOutcomes.Verify(
            source => source.ScriptNext(It.Is<ScriptedChargeOutcome>(o => o.Outcome == expected)), Times.Once);
    }

    [Fact]
    public async Task Refuses_to_advance_more_than_one_period()
    {
        SetAuthorizedCaller();

        var request = new AdvanceRenewalRequest { OrganizationId = "target-org", Periods = 2 };
        var result = await CreateService().AdvanceRenewalAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_periods_invalid");
        _contextResolver.Verify(
            resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Refuses_to_defer_scheduling_since_there_is_no_simulated_clock()
    {
        SetAuthorizedCaller();

        var request = new AdvanceRenewalRequest { OrganizationId = "target-org", RunImmediately = false };
        var result = await CreateService().AdvanceRenewalAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_scheduling_not_supported");
    }

    [Fact]
    public async Task Refuses_to_advance_a_renewal_for_an_incomplete_subscription()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Incomplete,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);

        var request = new AdvanceRenewalRequest { OrganizationId = "target-org" };
        var result = await CreateService().AdvanceRenewalAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_not_renewable");
    }

    private static SubscriptionDetail UsageSubscription() => new()
    {
        ItemId = SubscriptionId,
        TenantId = TenantId,
        OrganizationId = "target-org",
        Status = SubscriptionStatus.Active,
        CurrencyCode = "EUR",
        CurrentUsagePeriodStartUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CurrentUsagePeriodEndUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Closing_a_usage_period_with_no_overage_charges_nothing()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = UsageSubscription();
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _usageRatingProcessor
            .Setup(processor => processor.CloseSubscriptionPeriodsAsync(
                subscription, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _usageInvoices
            .Setup(repo => repo.GetAsync(TenantId, SubscriptionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionUsageInvoice?)null);
        StubEmptyState(subscription, "target-org");

        var request = new CloseUsagePeriodRequest { OrganizationId = "target-org" };
        var result = await CreateService().CloseUsagePeriodAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _usageRatingProcessor.Verify(
            processor => processor.ChargeInvoiceAsync(It.IsAny<SubscriptionUsageInvoice>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Closing_a_usage_period_with_overage_scripts_and_charges_the_invoice()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = UsageSubscription();
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _usageRatingProcessor
            .Setup(processor => processor.CloseSubscriptionPeriodsAsync(
                subscription, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var invoice = new SubscriptionUsageInvoice
        {
            ItemId = "invoice-1", TenantId = TenantId, SubscriptionId = SubscriptionId,
            State = SubscriptionUsageInvoiceState.Pending, TotalAmountMinor = 500, CurrencyCode = "EUR",
        };
        _usageInvoices
            .Setup(repo => repo.GetAsync(TenantId, SubscriptionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        StubEmptyState(subscription, "target-org");

        var request = new CloseUsagePeriodRequest { OrganizationId = "target-org", PaymentOutcome = SimulatedRenewalOutcome.Succeeded };
        var result = await CreateService().CloseUsagePeriodAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _scriptedOutcomes.Verify(
            source => source.ScriptNext(It.Is<ScriptedChargeOutcome>(o => o.Outcome == SimulatedChargeOutcome.Succeeded)),
            Times.Once);
        _usageRatingProcessor.Verify(
            processor => processor.ChargeInvoiceAsync(invoice, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Closing_a_usage_period_does_not_charge_when_chargeInvoice_is_false()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = UsageSubscription();
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _usageRatingProcessor
            .Setup(processor => processor.CloseSubscriptionPeriodsAsync(
                subscription, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _usageInvoices
            .Setup(repo => repo.GetAsync(TenantId, SubscriptionId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionUsageInvoice
            {
                ItemId = "invoice-1", State = SubscriptionUsageInvoiceState.Pending, TotalAmountMinor = 500,
            });
        StubEmptyState(subscription, "target-org");

        var request = new CloseUsagePeriodRequest { OrganizationId = "target-org", ChargeInvoice = false };
        var result = await CreateService().CloseUsagePeriodAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _usageRatingProcessor.Verify(
            processor => processor.ChargeInvoiceAsync(It.IsAny<SubscriptionUsageInvoice>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Reports_no_change_when_the_period_could_not_be_closed()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = UsageSubscription();
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _usageRatingProcessor
            .Setup(processor => processor.CloseSubscriptionPeriodsAsync(
                subscription, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        StubEmptyState(subscription, "target-org");

        var request = new CloseUsagePeriodRequest { OrganizationId = "target-org" };
        var result = await CreateService().CloseUsagePeriodAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _usageInvoices.Verify(
            repo => repo.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Refuses_to_defer_a_usage_close_since_there_is_no_simulated_clock()
    {
        SetAuthorizedCaller();

        var request = new CloseUsagePeriodRequest { OrganizationId = "target-org", RunImmediately = false };
        var result = await CreateService().CloseUsagePeriodAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_scheduling_not_supported");
    }

    [Fact]
    public async Task Running_due_jobs_reports_not_due_when_nothing_qualifies()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Active,
            NextFeeBillingAtUtc = DateTime.UtcNow.AddDays(10),
            CurrentUsagePeriodEndUtc = DateTime.UtcNow.AddDays(10),
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        _usageInvoices
            .Setup(repo => repo.ListBySubscriptionAsync(TenantId, SubscriptionId, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SubscriptionUsageInvoice>)[]);
        StubEmptyState(subscription, "target-org");

        var request = new RunDueJobsRequest { OrganizationId = "target-org" };
        var result = await CreateService().RunDueJobsAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Jobs.Should().HaveCount(4);
        result.Value!.Jobs.Should().OnlyContain(job => job.Status == "NotDue");
        result.Value!.Completed.Should().Be(0);
        _renewalService.Verify(
            service => service.RenewAsync(It.IsAny<SubscriptionDetail>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no scripted outcome exists for this path — a real gateway call must never fire when nothing is actually due");
    }

    [Fact]
    public async Task Running_due_jobs_runs_only_the_requested_work_types()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Active,
            NextFeeBillingAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CurrentUsagePeriodEndUtc = DateTime.UtcNow.AddDays(10),
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        StubEmptyState(subscription, "target-org");

        var request = new RunDueJobsRequest
        {
            OrganizationId = "target-org",
            WorkTypes = [SimulationWorkType.Renewal],
        };
        var result = await CreateService().RunDueJobsAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Jobs.Should().ContainSingle().Which.WorkType.Should().Be("Renewal");
        result.Value!.Jobs[0].Status.Should().Be("Completed");
        _renewalService.Verify(
            service => service.RenewAsync(subscription, It.IsAny<CancellationToken>()), Times.Once);
        _scriptedOutcomes.Verify(
            source => source.ScriptNext(It.IsAny<ScriptedChargeOutcome>()),
            Times.Never,
            "running due jobs must go to the real gateway, never a scripted outcome");
        // 50 is the job's own lookup limit — distinct from the 100 GetStateAsync uses when
        // assembling the response's trailing state snapshot, which is expected regardless.
        _usageInvoices.Verify(
            repo => repo.ListBySubscriptionAsync(
                It.IsAny<string>(), It.IsAny<string>(), 50, It.IsAny<CancellationToken>()),
            Times.Never,
            "only the requested work type should run");
    }

    [Fact]
    public async Task Running_due_jobs_publishes_outbox_events_and_charges_due_usage_invoices()
    {
        SetAuthorizedCaller();
        ResolvesContext("target-org");
        var subscription = new SubscriptionDetail
        {
            ItemId = SubscriptionId, TenantId = TenantId, OrganizationId = "target-org",
            Status = SubscriptionStatus.Active,
        };
        _subscriptions
            .Setup(repo => repo.GetAsync(TenantId, "target-org", SubscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        var dueInvoice = new SubscriptionUsageInvoice
        {
            ItemId = "invoice-1", State = SubscriptionUsageInvoiceState.Pending,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1),
        };
        _usageInvoices
            .Setup(repo => repo.ListBySubscriptionAsync(TenantId, SubscriptionId, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<SubscriptionUsageInvoice>)[dueInvoice]);
        _outboxProcessor
            .Setup(processor => processor.PublishDueForSubscriptionAsync(subscription, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        StubEmptyState(subscription, "target-org");

        var request = new RunDueJobsRequest
        {
            OrganizationId = "target-org",
            WorkTypes = [SimulationWorkType.UsageInvoiceCharge, SimulationWorkType.OutboxPublication],
        };
        var result = await CreateService().RunDueJobsAsync(
            SubscriptionId, request, CorrelationId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Completed.Should().Be(2);
        _usageRatingProcessor.Verify(
            processor => processor.ChargeInvoiceAsync(dueInvoice, It.IsAny<CancellationToken>()), Times.Once);
        _outboxProcessor.Verify(
            processor => processor.PublishDueForSubscriptionAsync(subscription, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
