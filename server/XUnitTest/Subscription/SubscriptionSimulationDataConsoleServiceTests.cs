using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Simulation;

namespace XUnitTest.Subscription;

/// <summary>
/// <see cref="SubscriptionSimulationDataConsoleService"/>'s guard, policy and validation surface
/// — everything that must reject a call before it ever reaches Mongo.
/// </summary>
/// <remarks>
/// None of these tests configure <see cref="IDbContextProvider"/>: every scenario here is
/// rejected before the service would call it, which is itself part of what is under test — an
/// unauthorized or malformed call must never reach a database round trip.
/// </remarks>
public sealed class SubscriptionSimulationDataConsoleServiceTests : IDisposable
{
    private const string ConsoleOrganizationId = "console-org";
    private const string SubscriptionId = "sub-1";
    private const string TenantId = "tenant-1";
    private const string CorrelationId = "corr-1";

    private readonly Mock<ISubscriptionContextResolver> _contextResolver = new();
    private readonly Mock<IDbContextProvider> _db = new();
    private readonly Mock<ISubscriptionSimulationRunRepository> _simulationRuns = new();
    private readonly Mock<IOptionsMonitor<PaymentOptions>> _paymentOptions = new();

    public SubscriptionSimulationDataConsoleServiceTests() =>
        _paymentOptions
            .Setup(options => options.CurrentValue)
            .Returns(new PaymentOptions { ConsoleOrganizationId = ConsoleOrganizationId });

    public void Dispose() => BlocksContext.ClearContext();

    [Fact]
    public async Task Find_refuses_a_caller_who_is_not_the_console()
    {
        SetCaller(organizationId: "some-other-org", permissions: [SubscriptionSimulationGuard.SimulationAdministratorPermission]);

        var result = await CreateService().FindAsync(
            "subscriptions",
            new FindDataRequest { OrganizationId = "target-org", SubscriptionId = SubscriptionId },
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_forbidden");
        _contextResolver.Verify(
            resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Find_refuses_the_console_without_the_simulation_permission()
    {
        SetCaller(organizationId: ConsoleOrganizationId, permissions: []);

        var result = await CreateService().FindAsync(
            "subscriptions",
            new FindDataRequest { OrganizationId = "target-org", SubscriptionId = SubscriptionId },
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_forbidden");
    }

    [Fact]
    public async Task Find_rejects_a_collection_that_is_not_allowlisted()
    {
        SetAuthorizedCaller();

        var result = await CreateService().FindAsync(
            "not-a-real-collection",
            new FindDataRequest { OrganizationId = "target-org", SubscriptionId = SubscriptionId },
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_collection_not_allowed");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task Find_rejects_an_out_of_range_limit(int limit)
    {
        SetAuthorizedCaller();

        var result = await CreateService().FindAsync(
            "subscriptions",
            new FindDataRequest { OrganizationId = "target-org", SubscriptionId = SubscriptionId, Limit = limit },
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_limit_invalid");
    }

    /// <summary>
    /// Naming no organization is resolved as the console's own, not refused. See the matching
    /// test on <c>SubscriptionSimulationServiceTests</c> for why.
    /// </summary>
    [Fact]
    public async Task Find_resolves_a_blank_organization_rather_than_refusing_it()
    {
        SetAuthorizedCaller();
        _contextResolver
            .Setup(resolver => resolver.ResolveAsync(
                CorrelationId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Unavailable,
                "subscription_organization_missing",
                "An organization is required to resolve a subscription."));

        var result = await CreateService().FindAsync(
            "subscriptions",
            new FindDataRequest { OrganizationId = null, SubscriptionId = SubscriptionId },
            CorrelationId,
            CancellationToken.None);

        result.ErrorCode.Should().Be(
            "subscription_organization_missing",
            "the resolver's own answer, not a refusal the harness invented first");
        _contextResolver.Verify(
            resolver => resolver.ResolveAsync(
                CorrelationId, null, It.IsAny<CancellationToken>()),
            Times.Once,
            "the blank organization is resolved, not refused before resolving");
    }

    [Fact]
    public async Task Update_rejects_a_collection_that_is_not_allowlisted()
    {
        SetAuthorizedCaller();

        var result = await CreateService().UpdateFieldsAsync(
            "not-a-real-collection",
            new UpdateDataFieldRequest
            {
                OrganizationId = "target-org",
                SubscriptionId = SubscriptionId,
                Fields = new Dictionary<string, string> { ["NextFeeBillingAtUtc"] = "2026-01-01T00:00:00Z" }
            },
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_collection_not_allowed");
    }

    [Fact]
    public async Task Update_rejects_an_empty_field_set()
    {
        SetAuthorizedCaller();

        var result = await CreateService().UpdateFieldsAsync(
            "subscriptions",
            new UpdateDataFieldRequest { OrganizationId = "target-org", SubscriptionId = SubscriptionId },
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_no_fields");
    }

    [Fact]
    public async Task Update_rejects_a_field_the_policy_does_not_allow()
    {
        SetAuthorizedCaller();

        var result = await CreateService().UpdateFieldsAsync(
            "subscriptions",
            new UpdateDataFieldRequest
            {
                OrganizationId = "target-org",
                SubscriptionId = SubscriptionId,
                Fields = new Dictionary<string, string> { ["Status"] = "Active" }
            },
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_field_not_allowed");
    }

    [Fact]
    public async Task Update_rejects_a_value_that_is_not_a_utc_timestamp()
    {
        SetAuthorizedCaller();

        var result = await CreateService().UpdateFieldsAsync(
            "subscriptions",
            new UpdateDataFieldRequest
            {
                OrganizationId = "target-org",
                SubscriptionId = SubscriptionId,
                Fields = new Dictionary<string, string> { ["NextFeeBillingAtUtc"] = "not-a-date" }
            },
            CorrelationId,
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_simulation_field_value_invalid");
    }

    [Fact]
    public async Task Update_validates_fields_before_ever_resolving_the_caller_s_context()
    {
        SetAuthorizedCaller();

        await CreateService().UpdateFieldsAsync(
            "subscriptions",
            new UpdateDataFieldRequest
            {
                OrganizationId = "target-org",
                SubscriptionId = SubscriptionId,
                Fields = new Dictionary<string, string> { ["NextFeeBillingAtUtc"] = "not-a-date" }
            },
            CorrelationId,
            CancellationToken.None);

        _contextResolver.Verify(
            resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an unparsable field must be rejected before the harness ever touches the database");
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

    private SubscriptionSimulationDataConsoleService CreateService() => new(
        _contextResolver.Object,
        _db.Object,
        _simulationRuns.Object,
        _paymentOptions.Object);
}
