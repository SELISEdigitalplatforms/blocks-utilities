using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription;

/// <summary>
/// Who a tenant issues its invoices and credit notes under.
/// </summary>
/// <remarks>
/// The properties worth pinning are all about who decides. An invoice names a seller in law, so a
/// subscriber must not be able to set it, and one deployment serving many tenants must not have them
/// all issuing under the same company.
/// </remarks>
public sealed class SubscriptionMerchantProfileTests
{
    private const string TenantId = "tenant-1";
    private const string ConsoleOrganizationId = "console";
    private const string SubscriberOrganizationId = "org-1";

    private readonly Mock<ISubscriptionContextResolver> _context = new();
    private readonly Mock<ISubscriptionMerchantProfileRepository> _profiles = new();

    private SubscriptionMerchantProfile? _stored;

    public SubscriptionMerchantProfileTests()
    {
        // The console unless a test says otherwise, because that is the caller almost every one of
        // these is about. Read lazily by the mock, so a test may override it at any point.
        Caller(ConsoleOrganizationId);

        _profiles
            .Setup(profiles => profiles.GetAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored);

        _profiles
            .Setup(profiles => profiles.UpsertAsync(
                It.IsAny<SubscriptionMerchantProfile>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionMerchantProfile profile, CancellationToken _) =>
            {
                _stored = profile;

                return profile;
            });
    }

    [Fact]
    public async Task Each_tenant_issues_under_its_own_seller()
    {
        _stored = new SubscriptionMerchantProfile
        {
            TenantId = TenantId,
            LegalName = "Northwind Software GmbH",
            TaxRegistrationId = "DE811234567",
            PaymentInstructions = "IBAN DE00 1234"
        };

        var merchant = await Service().ResolveAsync(TenantId, CancellationToken.None);

        // Not the configured identity. One deployment serves many tenants, and every one of them
        // issuing invoices under a single company's legal name and tax registration is a false
        // statement on a financial record, not a letterhead problem.
        merchant.LegalName.Should().Be("Northwind Software GmbH");
        merchant.TaxRegistrationId.Should().Be("DE811234567");
        merchant.PaymentInstructions.Should().Be("IBAN DE00 1234");
    }

    [Fact]
    public async Task A_tenant_that_has_never_set_one_falls_back_to_configuration()
    {
        var merchant = await Service().ResolveAsync(TenantId, CancellationToken.None);

        // Kept rather than removed so upgrading does not silently blank the seller on every document
        // issued between the deployment and somebody noticing.
        merchant.LegalName.Should().Be("Blocks AG");
    }

    [Fact]
    public async Task Resolving_never_refuses_because_the_money_has_already_moved()
    {
        var merchant = await Service(configuredLegalName: string.Empty)
            .ResolveAsync(TenantId, CancellationToken.None);

        // By the time a document is being composed the charge has settled. Refusing to record it
        // because nobody filled in a form would lose the record of a real payment; enforcement belongs
        // before the charge, where refusing costs nothing.
        merchant.LegalName.Should().BeEmpty();
    }

    [Fact]
    public async Task A_tenant_naming_no_seller_at_all_blocks_a_charge_while_enforcement_is_on()
    {
        var missing = await Service(configuredLegalName: string.Empty)
            .MissingFieldsAsync(TenantId, CancellationToken.None);

        missing.Should().ContainSingle().Which.Should().Be("merchantLegalName");
    }

    [Fact]
    public async Task A_configured_seller_is_enough_to_charge_against()
    {
        var missing = await Service().MissingFieldsAsync(TenantId, CancellationToken.None);

        missing.Should().BeEmpty();
    }

    [Fact]
    public async Task Enforcement_off_asks_for_nothing()
    {
        var missing = await Service(configuredLegalName: string.Empty, required: false)
            .MissingFieldsAsync(TenantId, CancellationToken.None);

        missing.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_the_console_may_say_who_the_seller_is()
    {
        Caller(SubscriberOrganizationId);

        var result = await Service().UpdateAsync(
            NewRequest(),
            "corr-1",
            CancellationToken.None);

        // A subscriber able to set this could have their own invoices issued under a company of their
        // choosing, which is forgery with extra steps.
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_merchant_profile_forbidden");
        _profiles.Verify(
            profiles => profiles.UpsertAsync(
                It.IsAny<SubscriptionMerchantProfile>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task The_console_sets_it_and_the_stored_identity_takes_over_from_configuration()
    {
        Caller(ConsoleOrganizationId);

        var result = await Service().UpdateAsync(
            NewRequest(),
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.LegalName.Should().Be("Northwind Software GmbH");
        result.Value.IsComplete.Should().BeTrue();

        // No longer inherited, which is what a console needs to see: before this, the values it was
        // showing were shared with every other tenant in the deployment.
        result.Value.IsInheritedFromConfiguration.Should().BeFalse();

        var merchant = await Service().ResolveAsync(TenantId, CancellationToken.None);
        merchant.LegalName.Should().Be("Northwind Software GmbH");
    }

    [Fact]
    public async Task A_seller_with_no_legal_name_is_refused_before_it_can_be_stored()
    {
        Caller(ConsoleOrganizationId);

        var result = await Service().UpdateAsync(
            new UpdateMerchantProfileRequest { LegalName = "  " },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("subscription_merchant_profile_invalid");
    }

    [Fact]
    public async Task Reading_it_is_open_to_the_tenant_because_it_is_printed_on_their_invoices()
    {
        Caller(SubscriberOrganizationId);

        var result = await Service().GetAsync("corr-1", CancellationToken.None);

        // Withholding the seller's name from the subscriber it was already sent to would protect
        // nothing.
        result.IsSuccess.Should().BeTrue();
        result.Value!.LegalName.Should().Be("Blocks AG");
        result.Value.IsInheritedFromConfiguration.Should().BeTrue();
    }

    [Fact]
    public async Task An_address_whose_every_line_is_blank_is_stored_as_no_address()
    {
        Caller(ConsoleOrganizationId);

        await Service().UpdateAsync(
            new UpdateMerchantProfileRequest
            {
                LegalName = "Northwind Software GmbH",
                Address = new BillingAddressRequest { City = "   " }
            },
            "corr-1",
            CancellationToken.None);

        // So a document does not render an empty block that reads as missing data.
        _stored!.Address.Should().BeNull();
    }

    private void Caller(string organizationId) =>
        _context
            .Setup(context => context.ResolveAsync(
                It.IsAny<string>(),
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, organizationId, "actor-1", "user-1")));

    private static UpdateMerchantProfileRequest NewRequest() => new()
    {
        LegalName = "Northwind Software GmbH",
        TaxRegistrationId = "DE811234567"
    };

    private ISubscriptionMerchantProfileService Service(
        string configuredLegalName = "Blocks AG",
        bool required = true)
    {
        return new SubscriptionMerchantProfileService(
            _context.Object,
            _profiles.Object,
            new UpdateMerchantProfileRequestValidator(),
            Options.Create(new SubscriptionOptions
            {
                RequireBillingProfile = required,
                Invoicing = new SubscriptionInvoicingOptions { LegalName = configuredLegalName }
            }),
            Options.Create(new PaymentOptions
            {
                ConsoleOrganizationId = ConsoleOrganizationId
            }));
    }
}
