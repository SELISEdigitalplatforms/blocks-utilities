using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;
using Subscription.DomainService.Validators;

namespace XUnitTest.Subscription;

/// <summary>
/// The identity a subscriber's documents are addressed to, and the gate in front of the money.
/// </summary>
/// <remarks>
/// The gate exists because an invoice with a blank recipient is not a document anybody can use, and
/// the only moment it can be prevented is before the charge. Afterwards the invoice is owed whatever
/// the profile says.
/// </remarks>
public sealed class SubscriptionBillingProfileTests
{
    private const string TenantId = "tenant-1";
    private const string OrganizationId = "org-1";

    private readonly Mock<ISubscriptionBillingProfileRepository> _profiles = new();
    private readonly Mock<ISubscriptionContextResolver> _context = new();

    public SubscriptionBillingProfileTests()
    {
        _context
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, OrganizationId, "actor-1", "user-7")));

        _profiles
            .Setup(profiles => profiles.UpsertAsync(
                It.IsAny<SubscriptionBillingProfile>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionBillingProfile profile, CancellationToken _) => profile);
    }

    [Fact]
    public async Task An_organization_that_has_never_answered_gets_an_empty_profile_and_a_field_list()
    {
        _profiles
            .Setup(profiles => profiles.GetAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionBillingProfile?)null);

        var result = await Service().GetAsync(null, "corr-1", CancellationToken.None);

        // Not a 404: there is nothing missing about not having answered yet, and a client rendering a
        // form needs the same shape either way.
        result.IsSuccess.Should().BeTrue();
        result.Value!.IsComplete.Should().BeFalse();
        result.Value.MissingFields.Should().BeEquivalentTo(
        [
            nameof(SubscriptionBillingProfile.LegalName),
            nameof(SubscriptionBillingProfile.BillingContactName),
            nameof(SubscriptionBillingProfile.BillingContactEmail)
        ]);
    }

    [Fact]
    public async Task A_complete_profile_reports_no_missing_fields()
    {
        Existing();

        var result = await Service().GetAsync(null, "corr-1", CancellationToken.None);

        result.Value!.IsComplete.Should().BeTrue();
        result.Value.MissingFields.Should().BeEmpty();
    }

    [Fact]
    public async Task An_address_and_a_tax_id_are_optional()
    {
        // Many subscribers are individuals with neither, and refusing them a subscription over a
        // field their jurisdiction does not ask for would be a billing rule invented here.
        Existing(profile =>
        {
            profile.Address = null;
            profile.TaxRegistrationId = null;
        });

        (await Service().GetAsync(null, "corr-1", CancellationToken.None))
            .Value!.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Updating_normalises_the_contact_email_and_the_country_code()
    {
        SubscriptionBillingProfile? written = null;
        _profiles
            .Setup(profiles => profiles.UpsertAsync(
                It.IsAny<SubscriptionBillingProfile>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionBillingProfile profile, CancellationToken _) =>
            {
                written = profile;

                return profile;
            });

        await Service().UpdateAsync(
            new UpdateBillingProfileRequest
            {
                LegalName = "  Northwind Trading AG  ",
                BillingContactName = " Ada Byron ",
                BillingContactEmail = "  Ada@Northwind.Example ",
                Address = new BillingAddressRequest { Line1 = "1 Bahnhofstrasse", CountryCode = "ch" }
            },
            "corr-1",
            CancellationToken.None);

        written!.LegalName.Should().Be("Northwind Trading AG");
        written.BillingContactEmail.Should().Be("ada@northwind.example");
        written.Address!.CountryCode.Should().Be("CH");
    }

    [Fact]
    public async Task An_address_whose_every_line_is_blank_is_stored_as_no_address()
    {
        SubscriptionBillingProfile? written = null;
        _profiles
            .Setup(profiles => profiles.UpsertAsync(
                It.IsAny<SubscriptionBillingProfile>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionBillingProfile profile, CancellationToken _) =>
            {
                written = profile;

                return profile;
            });

        await Service().UpdateAsync(
            new UpdateBillingProfileRequest
            {
                LegalName = "Northwind Trading AG",
                BillingContactName = "Ada Byron",
                BillingContactEmail = "ada@northwind.example",
                Address = new BillingAddressRequest { Line1 = "  ", City = "" }
            },
            "corr-1",
            CancellationToken.None);

        // So a document does not render an empty block that looks like missing data.
        written!.Address.Should().BeNull();
    }

    [Fact]
    public async Task Whoever_sets_the_profile_up_is_remembered_as_somebody_a_document_can_name()
    {
        Existing();

        await Service().UpdateAsync(
            new UpdateBillingProfileRequest
            {
                LegalName = "Northwind Trading AG",
                BillingContactName = "Ada Byron",
                BillingContactEmail = "ada@northwind.example"
            },
            "corr-1",
            CancellationToken.None);

        _profiles.Verify(
            profiles => profiles.RecordContactAsync(
                TenantId,
                OrganizationId,
                It.Is<BillingContact>(contact => contact.UserId == "user-7"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("", "Ada", "ada@northwind.example")]
    [InlineData("Northwind", "", "ada@northwind.example")]
    [InlineData("Northwind", "Ada", "")]
    [InlineData("Northwind", "Ada", "not-an-email")]
    public async Task An_invalid_profile_is_refused_with_the_fields_that_failed(
        string legalName,
        string contactName,
        string contactEmail)
    {
        var result = await Service().UpdateAsync(
            new UpdateBillingProfileRequest
            {
                LegalName = legalName,
                BillingContactName = contactName,
                BillingContactEmail = contactEmail
            },
            "corr-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_billing_profile_invalid");
        result.ValidationErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_guard_names_the_fields_a_money_moving_change_still_needs()
    {
        _profiles
            .Setup(profiles => profiles.GetAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionBillingProfile
            {
                LegalName = "Northwind Trading AG",
                BillingContactName = string.Empty,
                BillingContactEmail = string.Empty
            });

        var missing = await Guard().MissingFieldsAsync(
            TenantId,
            OrganizationId,
            CancellationToken.None);

        missing.Should().BeEquivalentTo(
        [
            nameof(SubscriptionBillingProfile.BillingContactName),
            nameof(SubscriptionBillingProfile.BillingContactEmail)
        ]);
    }

    [Fact]
    public async Task The_guard_lets_everything_through_when_the_requirement_is_switched_off()
    {
        _profiles
            .Setup(profiles => profiles.GetAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionBillingProfile?)null);

        // The escape hatch for an installation mid-migration, where subscribers predate the profile
        // and refusing their changes would be worse than a document addressed to an organization id.
        var missing = await Guard(required: false).MissingFieldsAsync(
            TenantId,
            OrganizationId,
            CancellationToken.None);

        missing.Should().BeEmpty();
        _profiles.Verify(
            profiles => profiles.GetAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_contact_that_cannot_be_recorded_does_not_fail_the_operation()
    {
        Existing();
        _profiles
            .Setup(profiles => profiles.RecordContactAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<BillingContact>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("mongo is having a moment"));

        // Failing to remember a name must never fail a change the subscriber asked for.
        var act = async () => await Guard().RememberInitiatorAsync(
            TenantId, OrganizationId, "user-7", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private ISubscriptionBillingProfileService Service() =>
        new SubscriptionBillingProfileService(
            _context.Object,
            _profiles.Object,
            new UpdateBillingProfileRequestValidator());

    private ISubscriptionBillingProfileGuard Guard(bool required = true) =>
        new SubscriptionBillingProfileGuard(
            _profiles.Object,
            Options.Create(new SubscriptionOptions { RequireBillingProfile = required }),
            NullLogger<SubscriptionBillingProfileGuard>.Instance);

    private void Existing(Action<SubscriptionBillingProfile>? customize = null)
    {
        var profile = new SubscriptionBillingProfile
        {
            TenantId = TenantId,
            OrganizationId = OrganizationId,
            LegalName = "Northwind Trading AG",
            BillingContactName = "Ada Byron",
            BillingContactEmail = "ada@northwind.example",
            Address = new BillingAddress { Line1 = "1 Bahnhofstrasse", CountryCode = "CH" },
            TaxRegistrationId = "CHE-123.456.789"
        };

        customize?.Invoke(profile);

        _profiles
            .Setup(profiles => profiles.GetAsync(
                TenantId, OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
    }
}
