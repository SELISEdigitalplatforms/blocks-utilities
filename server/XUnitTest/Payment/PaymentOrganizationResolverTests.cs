using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// One policy, shared by provider registration and payment creation. Two copies would be two
/// policies, and the difference would only surface as one endpoint trusting an organization
/// the other refuses.
/// </summary>
public sealed class PaymentOrganizationResolverTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<IOrganizationDirectory> _organizations = new();

    public PaymentOrganizationResolverTests()
    {
        _organizations.Setup(x => x.FindAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.Found);
    }

    [Fact]
    public async Task Naming_nothing_takes_the_callers_organization_without_asking_iam()
    {
        var result = await Resolver().ResolveAsync(
            null,
            Context("organization-1"),
            "corr",
            CancellationToken.None);

        result.Failure.Should().BeNull();
        result.OrganizationId.Should().Be("organization-1");
        _organizations.Verify(
            x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_blank_organization_is_treated_as_naming_nothing()
    {
        var result = await Resolver().ResolveAsync(
            "   ",
            Context("organization-1"),
            "corr",
            CancellationToken.None);

        result.OrganizationId.Should().Be("organization-1");
        _organizations.Verify(
            x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Naming_the_callers_own_organization_needs_no_verification()
    {
        var result = await Resolver().ResolveAsync(
            "organization-1",
            Context("organization-1"),
            "corr",
            CancellationToken.None);

        result.OrganizationId.Should().Be("organization-1");
        _organizations.Verify(
            x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_verified_organization_is_accepted()
    {
        var result = await Resolver().ResolveAsync(
            "organization-2",
            Context("default"),
            "corr",
            CancellationToken.None);

        result.Failure.Should().BeNull();
        result.OrganizationId.Should().Be("organization-2");
    }

    [Fact]
    public async Task An_unknown_organization_is_refused()
    {
        _organizations.Setup(x => x.FindAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.NotFound);

        var result = await Resolver().ResolveAsync(
            "no-such-organization",
            Context("default"),
            "corr",
            CancellationToken.None);

        result.OrganizationId.Should().BeNull();
        result.Failure!.ErrorCode.Should().Be("organization_not_found");
        result.Failure.FailureKind.Should().Be(PaymentFailureKind.Validation);
    }

    [Fact]
    public async Task An_unverifiable_organization_fails_closed()
    {
        _organizations.Setup(x => x.FindAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.Unavailable);

        var result = await Resolver().ResolveAsync(
            "organization-2",
            Context("default"),
            "corr",
            CancellationToken.None);

        result.OrganizationId.Should().BeNull();
        result.Failure!.ErrorCode.Should().Be("organization_verification_unavailable");
        result.Failure.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
    }

    /// <summary>
    /// The temporary bypass. It accepts whatever the caller names without asking IAM, which
    /// is a real gap: within the tenant, a caller can now attach a merchant account to an
    /// organization that is not theirs. The tenant still comes from the token, so nothing
    /// crosses a tenant boundary.
    /// </summary>
    [Fact]
    public async Task Verification_can_be_switched_off_and_then_nothing_is_asked()
    {
        var result = await Resolver(verify: false).ResolveAsync(
            "organization-2",
            Context("default"),
            "corr",
            CancellationToken.None);

        result.Failure.Should().BeNull();
        result.OrganizationId.Should().Be("organization-2");
        _organizations.Verify(
            x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static PaymentExecutionContext Context(string? organizationId) =>
        new(TenantId, "actor-1", organizationId);

    private PaymentOrganizationResolver Resolver(bool verify = true)
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.SetupGet(x => x.CurrentValue)
            .Returns(new PaymentOptions { VerifyOrganizationWithIam = verify });

        return new PaymentOrganizationResolver(
            _organizations.Object,
            options.Object,
            NullLogger<PaymentOrganizationResolver>.Instance);
    }
}
