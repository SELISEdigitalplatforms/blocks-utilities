using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;

namespace XUnitTest.Payment;

/// <summary>
/// Asserts the shape of the saved-card lookup filter without a database.
/// </summary>
/// <remarks>
/// Pairing each shopper reference to its organization is the whole protection, and getting it
/// wrong produces a filter that still returns plausible results — just too many of them. A
/// single-organization fixture cannot show that, so the query is asserted directly.
/// </remarks>
public sealed class StoredPaymentMethodLookupFilterTests
{
    [Fact]
    public void Each_scope_matches_its_reference_and_organization_together()
    {
        var alternatives = Alternatives(
            new StoredPaymentMethodLookupScope("shopper-1", "organization-1"),
            new StoredPaymentMethodLookupScope("shopper-2", "organization-2"));

        alternatives.Should().HaveCount(2);

        // Both fields are pinned inside one alternative, so no alternative can be satisfied by
        // one scope's reference alongside another scope's organization.
        alternatives.Should().OnlyContain(alternative =>
            alternative.Names.Count() == 2 &&
            alternative.Contains("ShopperReference") &&
            alternative.Contains("OrganizationId"));

        Pairs(alternatives).Should().BeEquivalentTo(
            new[]
            {
                ("shopper-1", "organization-1"),
                ("shopper-2", "organization-2")
            });
    }

    /// <summary>
    /// The failure this guards against: an <c>$in</c> over references and a separate <c>$in</c>
    /// over organizations reads as equivalent, and admits every combination of the two.
    /// </summary>
    [Fact]
    public void The_filter_does_not_admit_a_crossed_reference_and_organization()
    {
        var pairs = Pairs(
            Alternatives(
                new StoredPaymentMethodLookupScope("shopper-1", "organization-1"),
                new StoredPaymentMethodLookupScope("shopper-2", "organization-2")));

        pairs.Should().NotContain(("shopper-1", "organization-2"));
        pairs.Should().NotContain(("shopper-2", "organization-1"));
    }

    /// <summary>
    /// The tenant-level scope must match a missing organization — where every card saved before
    /// organizations existed lives — rather than matching any organization.
    /// </summary>
    [Fact]
    public void The_tenant_level_scope_matches_a_null_organization()
    {
        var alternative = Alternatives(
            new StoredPaymentMethodLookupScope("shopper-1", null)).Single();

        alternative["ShopperReference"].AsString.Should().Be("shopper-1");
        alternative["OrganizationId"].IsBsonNull.Should().BeTrue();
    }

    [Fact]
    public void The_filter_is_still_scoped_to_the_tenant_and_to_active_cards()
    {
        var rendered = Render(
            new StoredPaymentMethodLookupScope("shopper-1", "organization-1"));

        rendered["TenantId"].AsString.Should().Be("tenant");
        rendered.Contains("Status").Should().BeTrue();
    }

    private static List<(string?, string?)> Pairs(
        IEnumerable<BsonDocument> alternatives) =>
        alternatives
            .Select(alternative => (
                Value(alternative, "ShopperReference"),
                Value(alternative, "OrganizationId")))
            .ToList();

    private static string? Value(BsonDocument alternative, string field) =>
        alternative[field].IsBsonNull ? null : alternative[field].AsString;

    private static List<BsonDocument> Alternatives(
        params StoredPaymentMethodLookupScope[] scopes) =>
        Render(scopes)["$or"].AsBsonArray
            .Select(alternative => alternative.AsBsonDocument)
            .ToList();

    private static BsonDocument Render(
        params StoredPaymentMethodLookupScope[] scopes)
    {
        var serializer = BsonSerializer
            .SerializerRegistry.GetSerializer<StoredPaymentMethod>();

        return StoredPaymentMethodRepository
            .BuildActiveFilter("tenant", scopes)
            .Render(new RenderArgs<StoredPaymentMethod>(
                serializer,
                BsonSerializer.SerializerRegistry));
    }
}
