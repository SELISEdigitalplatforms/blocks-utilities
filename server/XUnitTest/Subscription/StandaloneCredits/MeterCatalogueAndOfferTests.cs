using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Enums;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Services;
using Subscription.DomainService.Validators;
using XUnitTest.Payment;

namespace XUnitTest.Subscription.StandaloneCredits;

/// <summary>
/// Authoring the meter catalogue and credit offers (spec 005).
/// </summary>
/// <remarks>
/// Guards against an offer that sells a meter nobody defined, a plan whose meters never reach the
/// catalogue, an archived meter taking new offers, an edit that silently rewrites the tenant's
/// entry when an organization's was meant, and an override that fails to win for its own organization.
/// </remarks>
public sealed class MeterCatalogueAndOfferTests
{
    private const string TenantId = "tenant-1";
    private const string ConsoleOrganizationId = "console-org";

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ISubscriptionContextResolver> _context = new();
    private readonly InMemoryMeterCatalogue _catalogue = new();
    private readonly InMemoryCreditOffers _offers = new();

    public MeterCatalogueAndOfferTests()
    {
        // The console: a named organization is honoured, none means the tenant scope.
        _context
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string? organizationId, CancellationToken _) =>
                SubscriptionContextResolution.Resolved(
                    new SubscriptionContext(TenantId, organizationId ?? ConsoleOrganizationId, "actor-1", "user-1")));
    }

    // ---- catalogue --------------------------------------------------------------------------

    [Fact]
    public async Task A_catalogue_meter_is_created_at_the_tenant_scope_when_no_organization_is_named()
    {
        var result = await Catalogue().CreateAsync(NewMeter(), "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value!.OrganizationId.Should().BeNull("the tenant's entry is the one every organization falls back to");
        result.Value.Status.Should().Be("Active");
    }

    [Fact]
    public async Task The_same_key_twice_at_one_scope_is_refused()
    {
        var service = Catalogue();
        await service.CreateAsync(NewMeter(), "corr-1", CancellationToken.None);

        var again = await service.CreateAsync(NewMeter(), "corr-2", CancellationToken.None);

        again.FailureKind.Should().Be(PaymentFailureKind.Conflict);
        again.ErrorCode.Should().Be("subscription_meter_catalogue_exists", "AC-17: a key is reused only by editing its entry");
    }

    [Fact]
    public async Task An_organizations_override_wins_for_it_and_the_tenant_entry_answers_everyone_else()
    {
        var service = Catalogue();
        await service.CreateAsync(NewMeter(displayName: "AI credits"), "corr-1", CancellationToken.None);
        await service.CreateAsync(NewMeter(displayName: "Acme AI credits", organizationId: "acme"), "corr-2", CancellationToken.None);

        var acme = await service.GetAsync("ai", "acme", "corr-3", CancellationToken.None);
        var other = await service.GetAsync("ai", "globex", "corr-4", CancellationToken.None);

        acme.Value!.DisplayName.Should().Be("Acme AI credits", "AC-26: an organization's own entry wins");
        other.Value!.DisplayName.Should().Be("AI credits", "AC-26: without an override the tenant's entry resolves");
    }

    [Fact]
    public async Task Editing_through_an_organization_without_an_override_does_not_rewrite_the_tenant_entry()
    {
        var service = Catalogue();
        await service.CreateAsync(NewMeter(), "corr-1", CancellationToken.None);

        var edit = await service.UpdateAsync("ai", Update("Renamed"), "acme", "corr-2", CancellationToken.None);

        edit.ErrorCode.Should().Be("subscription_meter_catalogue_not_found",
            "an edit addresses exactly the scope named; the fallback is for reading, never for writing");
        _catalogue.All.Single().DisplayName.Should().Be("AI credits");
    }

    [Fact]
    public async Task Archiving_keeps_the_entry_readable_and_refuses_a_second_archive()
    {
        var service = Catalogue();
        await service.CreateAsync(NewMeter(), "corr-1", CancellationToken.None);

        var archived = await service.UpdateAsync("ai", Update(status: CatalogueStatus.Archived), null, "corr-2", CancellationToken.None);
        var again = await service.UpdateAsync("ai", Update(status: CatalogueStatus.Archived), null, "corr-3", CancellationToken.None);
        var read = await service.GetAsync("ai", null, "corr-4", CancellationToken.None);

        archived.Value!.Status.Should().Be("Archived");
        again.ErrorCode.Should().Be("subscription_meter_catalogue_archived");
        read.IsSuccess.Should().BeTrue("AC-17: an archived meter stays readable, because plans and offers still point at it");
    }

    [Fact]
    public async Task A_plan_saved_with_a_new_meter_key_adds_it_to_the_catalogue_and_leaves_known_keys_alone()
    {
        var service = Catalogue();
        await service.CreateAsync(NewMeter(displayName: "AI credits"), "corr-1", CancellationToken.None);

        await service.EnsureEntriesAsync(
            TenantId,
            null,
            [
                new PlanMeter { MeterKey = "ai", DisplayName = "Plan's own name", UnitLabel = "credit" },
                new PlanMeter { MeterKey = "storage", DisplayName = "Storage", UnitLabel = "GB", QuantityScale = 2 }
            ],
            CancellationToken.None);

        _catalogue.All.Should().HaveCount(2, "AC-18: a meter authored inside a plan joins the catalogue");
        _catalogue.All.Single(entry => entry.MeterKey == "ai").DisplayName.Should().Be("AI credits",
            "an existing entry is the catalogue's word, not the plan's");
        var storage = _catalogue.All.Single(entry => entry.MeterKey == "storage");
        storage.UnitLabel.Should().Be("GB");
        storage.QuantityScale.Should().Be(2);
    }

    // ---- offers -------------------------------------------------------------------------------

    [Fact]
    public async Task An_offer_needs_a_catalogue_meter_and_refuses_an_archived_one()
    {
        var offers = Offers();

        var missing = await offers.CreateAsync(NewOffer(), "corr-1", CancellationToken.None);
        missing.ErrorCode.Should().Be("subscription_meter_catalogue_not_found", "an offer sells a meter the catalogue names");

        await Catalogue().CreateAsync(NewMeter(), "corr-2", CancellationToken.None);
        await Catalogue().UpdateAsync("ai", Update(status: CatalogueStatus.Archived), null, "corr-3", CancellationToken.None);

        var archived = await offers.CreateAsync(NewOffer(), "corr-4", CancellationToken.None);
        archived.ErrorCode.Should().Be("subscription_meter_catalogue_archived", "AC-17: no new offer on an archived meter");
    }

    [Fact]
    public async Task An_offer_is_one_per_meter_per_scope_and_an_edit_raises_its_version()
    {
        await Catalogue().CreateAsync(NewMeter(), "corr-0", CancellationToken.None);
        var offers = Offers();

        var created = await offers.CreateAsync(NewOffer(), "corr-1", CancellationToken.None);
        var duplicate = await offers.CreateAsync(NewOffer(), "corr-2", CancellationToken.None);
        var edited = await offers.UpdateAsync("ai", NewOffer(unitAmountMinor: 8), null, "corr-3", CancellationToken.None);

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        duplicate.ErrorCode.Should().Be("subscription_credit_offer_exists");
        edited.Value!.Version.Should().Be(created.Value!.Version + 1,
            "a purchase records the version it was quoted from, so every edit must move it");
        edited.Value.UnitPrices.Single().UnitAmountMinor.Should().Be(8);
    }

    [Fact]
    public async Task An_organizations_offer_override_wins_for_it_and_the_tenant_offer_answers_everyone_else()
    {
        await Catalogue().CreateAsync(NewMeter(), "corr-0", CancellationToken.None);
        var offers = Offers();
        await offers.CreateAsync(NewOffer(unitAmountMinor: 10), "corr-1", CancellationToken.None);
        await offers.CreateAsync(NewOffer(unitAmountMinor: 8, organizationId: "acme"), "corr-2", CancellationToken.None);

        var acme = await offers.GetAsync("ai", "acme", "corr-3", CancellationToken.None);
        var other = await offers.GetAsync("ai", "globex", "corr-4", CancellationToken.None);

        acme.Value!.UnitPrices.Single().UnitAmountMinor.Should().Be(8, "AC-25: the override prices its own organization");
        other.Value!.UnitPrices.Single().UnitAmountMinor.Should().Be(10, "AC-25: everyone else pays the tenant's price");
        acme.Value.DisplayName.Should().Be("AI credits", "the catalogue names the meter the offer sells");
    }

    [Fact]
    public async Task An_offer_with_a_tax_rate_but_no_mode_is_invalid()
    {
        await Catalogue().CreateAsync(NewMeter(), "corr-0", CancellationToken.None);
        var request = NewOffer();
        request.TaxRateBasisPoints = 810;

        var result = await Offers().CreateAsync(request, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.Validation, "one rate with two possible totals is not a price");
    }

    // ---- helpers ------------------------------------------------------------------------------

    private MeterCatalogueService Catalogue() => new(
        _catalogue,
        _context.Object,
        new CreateMeterCatalogueEntryRequestValidator(),
        new UpdateMeterCatalogueEntryRequestValidator(),
        NullLogger<MeterCatalogueService>.Instance,
        time: new ControlledTimeProvider(Now));

    private CreditOfferService Offers() => new(
        _offers,
        _catalogue,
        _context.Object,
        new CreditOfferRequestValidator(),
        time: new ControlledTimeProvider(Now));

    private static CreateMeterCatalogueEntryRequest NewMeter(
        string displayName = "AI credits",
        string? organizationId = null) => new()
    {
        OrganizationId = organizationId,
        MeterKey = "ai",
        DisplayName = displayName,
        UnitLabel = "credit"
    };

    private static UpdateMeterCatalogueEntryRequest Update(
        string displayName = "AI credits",
        CatalogueStatus status = CatalogueStatus.Active) => new()
    {
        DisplayName = displayName,
        UnitLabel = "credit",
        Status = status
    };

    private static CreditOfferRequest NewOffer(long unitAmountMinor = 10, string? organizationId = null) => new()
    {
        OrganizationId = organizationId,
        MeterKey = "ai",
        UnitPrices = [new MeterTopUpPriceRequest { CurrencyCode = "usd", UnitAmountMinor = unitAmountMinor }],
        MinQuantity = 10,
        MaxQuantity = 1_000,
        ExpiresAfter = new CreditExpiryDurationRequest { Count = 30, Unit = CreditExpiryUnit.Day }
    };
}
