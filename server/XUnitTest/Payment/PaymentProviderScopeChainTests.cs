using FluentAssertions;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>Which configurations may serve a caller, and in what order.</summary>
public sealed class PaymentProviderScopeChainTests
{
    [Fact]
    public void An_organizations_own_configuration_is_tried_first()
    {
        var candidates = PaymentProviderScopeChain.Candidates("org-a", Options());

        // A tenant that has already configured an organization must not have that meaning
        // changed by this rule.
        candidates.Should().Equal("org-a", null, "default");
    }

    [Fact]
    public void The_console_is_not_queried_twice_for_its_own_callers()
    {
        var candidates = PaymentProviderScopeChain.Candidates("default", Options());

        candidates.Should().Equal("default", null);
    }

    [Fact]
    public void A_caller_with_no_organization_still_reaches_the_tenants_configuration()
    {
        PaymentProviderScopeChain.Candidates(null, Options())
            .Should().Equal(null, "default");
        PaymentProviderScopeChain.Candidates("   ", Options())
            .Should().Equal(null, "default");
    }

    [Fact]
    public void A_console_only_tenant_keeps_its_configuration_to_itself()
    {
        var options = Options();
        options.TreatConsoleOrganizationAsTenantWide = false;

        // The escape hatch for a platform-owned merchant account that the tenant's own
        // organizations must not reach.
        PaymentProviderScopeChain.Candidates("org-a", options)
            .Should().Equal("org-a", null);
    }

    [Fact]
    public void Turning_the_console_off_entirely_also_turns_this_off()
    {
        var options = Options();
        options.ConsoleOrganizationId = string.Empty;

        PaymentProviderScopeChain.Candidates("org-a", options)
            .Should().Equal("org-a", null);
    }

    [Fact]
    public void The_tenants_own_configuration_always_outranks_the_consoles()
    {
        // Ordering is the whole rule: a null-organization row predates scoping and is the
        // tenant's deliberate choice, so it must win over the console's.
        var candidates = PaymentProviderScopeChain.Candidates("org-a", Options()).ToList();

        candidates.FindIndex(candidate => candidate is null)
            .Should().BeLessThan(candidates.FindIndex(candidate => candidate == "default"));
    }

    [Fact]
    public void Options_are_required() =>
        FluentActions.Invoking(() => PaymentProviderScopeChain.Candidates("org-a", null!))
            .Should().Throw<ArgumentNullException>();

    private static PaymentOptions Options() => new()
    {
        ConsoleOrganizationId = TestPaymentOptions.ConsoleOrganizationId,
        TreatConsoleOrganizationAsTenantWide = true
    };
}
