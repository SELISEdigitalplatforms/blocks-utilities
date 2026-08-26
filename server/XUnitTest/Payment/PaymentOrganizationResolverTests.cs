using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// The rule deciding which organization a write lands under.
/// </summary>
/// <remarks>
/// Worth asserting directly because both ways of getting it wrong are silent. Trusting the
/// request too widely lets any caller in the tenant configure and charge on another
/// organization's merchant account; trusting it too narrowly leaves the console — whose own
/// organization is fixed for every tenant — unable to reach any organization but one.
/// </remarks>
public sealed class PaymentOrganizationResolverTests
{
    private readonly Mock<IOrganizationDirectory> _organizations = new();

    public PaymentOrganizationResolverTests() =>
        _organizations
            .Setup(x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.Found);

    [Fact]
    public async Task The_console_may_name_another_organization()
    {
        var resolution = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: TestPaymentOptions.ConsoleOrganizationId);

        resolution.Failure.Should().BeNull();
        resolution.OrganizationId.Should().Be("organization-2");
    }

    /// <summary>
    /// The token is the stronger claim, so an application's own organization wins over whatever
    /// its body says. Without this every consumer of the API could write into any organization
    /// in its tenant by adding one field.
    /// </summary>
    [Fact]
    public async Task An_application_naming_another_organization_stays_in_its_own()
    {
        var resolution = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: "organization-1");

        resolution.Failure.Should().BeNull();
        resolution.OrganizationId.Should().Be("organization-1");
    }

    /// <summary>
    /// Ignored, not rejected. An integration that has always sent an organization field keeps
    /// working, and one that starts sending somebody else's simply has no effect.
    /// </summary>
    [Fact]
    public async Task An_application_naming_another_organization_is_not_refused()
    {
        var resolution = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: "organization-1");

        resolution.Failure.Should().BeNull();
        _organizations.Verify(
            x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a request that cannot win is not worth a directory round trip");
    }

    /// <summary>
    /// A caller with no organization is an integration scoped to the whole tenant, not the
    /// console. Letting it name one would be a widening dressed up as a filter.
    /// </summary>
    [Fact]
    public async Task A_caller_without_an_organization_may_not_name_one()
    {
        var resolution = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: null);

        resolution.OrganizationId.Should().BeNull();
    }

    [Fact]
    public async Task Naming_nothing_takes_the_callers_own_organization()
    {
        var resolution = await ResolveAsync(
            requested: null,
            contextOrganizationId: "organization-1");

        resolution.OrganizationId.Should().Be("organization-1");
    }

    /// <summary>
    /// Only the console is flagged, and only it can produce a payment whose organization the
    /// caller chose. That is what distinguishes a simulation from a real charge afterwards.
    /// </summary>
    [Fact]
    public async Task Only_the_console_is_recorded_as_having_named_the_organization()
    {
        var console = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: TestPaymentOptions.ConsoleOrganizationId);
        var application = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: "organization-1");

        console.RequestNamedTheOrganization.Should().BeTrue();
        application.RequestNamedTheOrganization.Should().BeFalse();
    }

    /// <summary>
    /// The escape hatch for a tenant whose real organizations include the configured sentinel:
    /// move the console elsewhere, and the identifier stops carrying any privilege.
    /// </summary>
    [Fact]
    public async Task Moving_the_console_takes_the_privilege_with_it()
    {
        var resolution = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: TestPaymentOptions.ConsoleOrganizationId,
            options: new PaymentOptions
            {
                ConsoleOrganizationId = "console-only"
            });

        resolution.OrganizationId.Should().Be(TestPaymentOptions.ConsoleOrganizationId);
    }

    [Fact]
    public async Task An_empty_console_organization_lets_nobody_name_one()
    {
        var resolution = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: TestPaymentOptions.ConsoleOrganizationId,
            options: new PaymentOptions { ConsoleOrganizationId = string.Empty });

        resolution.OrganizationId.Should().Be(TestPaymentOptions.ConsoleOrganizationId);
    }

    /// <summary>
    /// The console's naming is still checked against IAM, so a typo cannot create a provider —
    /// and a key ring — under an organization that does not exist.
    /// </summary>
    [Fact]
    public async Task An_organization_the_directory_does_not_know_is_refused()
    {
        _organizations
            .Setup(x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.NotFound);

        var resolution = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: TestPaymentOptions.ConsoleOrganizationId);

        resolution.Failure!.ErrorCode.Should().Be("organization_not_found");
        resolution.OrganizationId.Should().BeNull();
    }

    [Fact]
    public async Task An_unverifiable_organization_fails_closed()
    {
        _organizations
            .Setup(x => x.FindAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OrganizationLookupOutcome.Unavailable);

        var resolution = await ResolveAsync(
            requested: "organization-2",
            contextOrganizationId: TestPaymentOptions.ConsoleOrganizationId);

        resolution.Failure!.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
    }

    private Task<PaymentOrganizationResolution> ResolveAsync(
        string? requested,
        string? contextOrganizationId,
        PaymentOptions? options = null) =>
        new PaymentOrganizationResolver(
                _organizations.Object,
                TestPaymentOptions.Monitor(options),
                NullLogger<PaymentOrganizationResolver>.Instance)
            .ResolveAsync(
                requested,
                new PaymentExecutionContext("tenant-1", "actor-1", contextOrganizationId),
                "corr",
                CancellationToken.None);
}
