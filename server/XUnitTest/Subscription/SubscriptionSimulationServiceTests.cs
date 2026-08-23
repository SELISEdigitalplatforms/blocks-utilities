using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
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

    public SubscriptionSimulationServiceTests()
    {
        _paymentOptions
            .Setup(options => options.CurrentValue)
            .Returns(new PaymentOptions { ConsoleOrganizationId = ConsoleOrganizationId });
    }

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
        _paymentOptions.Object);
}
