using FluentAssertions;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Services;
using XUnitTest.Payment;

namespace XUnitTest.Subscription;

/// <summary>
/// Who the subscription module trusts as the caller's organization.
/// </summary>
/// <remarks>
/// Thin by design: the actual policy — which caller may name an organization other than their
/// own — lives once in <see cref="IPaymentOrganizationResolver"/> and is exercised directly by
/// <see cref="XUnitTest.Payment.PaymentOrganizationResolverTests"/>. These tests exist to prove
/// the adapter wires that policy in correctly and keeps subscription's own, stricter rule: a
/// blank resolved organization is refused rather than left as an unscoped read.
/// </remarks>
public sealed class SubscriptionContextResolverTests
{
    private readonly Mock<IPaymentExecutionContextResolver> _paymentContext = new();

    [Fact]
    public async Task The_console_may_name_another_organization()
    {
        Configure(TestPaymentOptions.ConsoleOrganizationId);

        var resolution = await ResolveAsync(requestedOrganizationId: "org-2");

        resolution.IsSuccess.Should().BeTrue();
        resolution.Context!.OrganizationId.Should().Be("org-2");
    }

    [Fact]
    public async Task An_applications_own_organization_wins_over_its_request()
    {
        Configure("org-1");

        var resolution = await ResolveAsync(requestedOrganizationId: "org-2");

        resolution.IsSuccess.Should().BeTrue();
        resolution.Context!.OrganizationId.Should().Be("org-1");
    }

    [Fact]
    public async Task Naming_nothing_takes_the_callers_own_organization()
    {
        Configure("org-1");

        var resolution = await ResolveAsync(requestedOrganizationId: null);

        resolution.IsSuccess.Should().BeTrue();
        resolution.Context!.OrganizationId.Should().Be("org-1");
    }

    [Fact]
    public async Task Missing_tenant_context_fails_closed()
    {
        _paymentContext
            .Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                null,
                PaymentOperationResult.Failure(
                    PaymentFailureKind.Unavailable,
                    "payment_context_missing",
                    "Authenticated tenant context is unavailable.",
                    "corr")));

        var resolution = await ResolveAsync(requestedOrganizationId: null);

        resolution.IsSuccess.Should().BeFalse();
        resolution.ErrorCode.Should().Be("subscription_context_missing");
    }

    /// <summary>
    /// Unlike the payment resolver it wraps, a blank resolved organization is not a valid
    /// outcome here — a caller scoped to the whole tenant has nothing a subscription answer
    /// could mean.
    /// </summary>
    [Fact]
    public async Task A_caller_without_an_organization_is_refused()
    {
        Configure(organizationId: null);

        var resolution = await ResolveAsync(requestedOrganizationId: null);

        resolution.IsSuccess.Should().BeFalse();
        resolution.ErrorCode.Should().Be("subscription_organization_missing");
    }

    private void Configure(string? organizationId) =>
        _paymentContext
            .Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext("tenant-1", "actor-1", organizationId, "user-1"),
                null));

    private Task<SubscriptionContextResolution> ResolveAsync(string? requestedOrganizationId) =>
        new SubscriptionContextResolver(
                _paymentContext.Object,
                new PaymentOrganizationResolver(
                    Mock.Of<IOrganizationDirectory>(),
                    TestPaymentOptions.Monitor(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentOrganizationResolver>.Instance))
            .ResolveAsync("corr", requestedOrganizationId, CancellationToken.None);
}
