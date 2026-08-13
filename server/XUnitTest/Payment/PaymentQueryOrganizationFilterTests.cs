using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Models;
using Payment.DomainService.Repositories;

namespace XUnitTest.Payment;

/// <summary>
/// Asserts what the payment listing filter actually asks the database for.
/// </summary>
/// <remarks>
/// A wrong scope filter still returns plausible payments — just the wrong set — so it cannot be
/// spotted from a result that looks reasonable. The query is asserted directly instead.
/// </remarks>
public sealed class PaymentQueryOrganizationFilterTests
{
    /// <summary>
    /// An organization sees its own payments and the ones made before organizations existed,
    /// which belong to none and are the tenant's shared history. Excluding those would empty
    /// every console on the day a tenant is split.
    /// </summary>
    [Fact]
    public void An_organization_sees_its_own_payments_and_the_ones_predating_organizations()
    {
        var alternatives = OrganizationAlternatives(
            Render(organizationId: "organization-1"));

        alternatives.Should().HaveCount(2);
        alternatives.Should().Contain(value => value == "organization-1");
        alternatives.Should().Contain(value => value == null);
    }

    [Fact]
    public void Another_organizations_payments_are_not_matched()
    {
        var alternatives = OrganizationAlternatives(
            Render(organizationId: "organization-1"));

        alternatives.Should().NotContain(value => value == "organization-2");
    }

    /// <summary>
    /// A caller belonging to no organization is not narrowed at all: no organization clause is
    /// added, so the whole tenant is returned exactly as before this filter existed.
    /// </summary>
    [Fact]
    public void A_caller_without_an_organization_adds_no_organization_clause()
    {
        var rendered = Render(organizationId: null);

        rendered.ToString().Should().NotContain("OrganizationId");
        rendered["TenantId"].AsString.Should().Be("tenant-1");
    }

    /// <summary>
    /// The scope is a filter on top of the caller's own criteria, not a replacement for them —
    /// an organization clause that dropped the other filters would widen every query.
    /// </summary>
    [Fact]
    public void The_organization_clause_is_added_alongside_the_other_filters()
    {
        var rendered = Render(
            organizationId: "organization-1",
            currencyCode: "CHF");

        rendered.ToString().Should().Contain("CHF");
        rendered["TenantId"].AsString.Should().Be("tenant-1");
        OrganizationAlternatives(rendered).Should().HaveCount(2);
    }

    private static List<string?> OrganizationAlternatives(BsonDocument rendered)
    {
        var clause = rendered.Contains("$or")
            ? rendered["$or"]
            : rendered["$and"].AsBsonArray
                .Select(element => element.AsBsonDocument)
                .Single(element => element.Contains("$or"))["$or"];

        return clause.AsBsonArray
            .Select(alternative =>
            {
                var value = alternative.AsBsonDocument["OrganizationId"];

                return value.IsBsonNull ? null : value.AsString;
            })
            .ToList();
    }

    /// <summary>
    /// The filter narrows; it must never widen. A caller scoped to one organization who asks
    /// for another's payments keeps their own visibility clause <em>and</em> gains the
    /// requested equality, so the two intersect to nothing and they see an empty page rather
    /// than someone else's data.
    /// </summary>
    [Fact]
    public void Filtering_for_another_organization_keeps_the_callers_own_scope()
    {
        var rendered = Render(
            organizationId: "organization-1",
            filterOrganizationId: "organization-2");

        var conditions = Conditions(rendered);

        // The caller's visibility clause survives...
        conditions.Should().ContainSingle(condition =>
            condition.Contains("\"$or\"", StringComparison.Ordinal) &&
            condition.Contains("organization-1", StringComparison.Ordinal));

        // ...alongside, not replaced by, the requested narrowing.
        conditions.Should().ContainSingle(condition =>
            !condition.Contains("\"$or\"", StringComparison.Ordinal) &&
            condition.Contains("organization-2", StringComparison.Ordinal));
    }

    /// <summary>
    /// A tenant-level caller already sees every organization, so narrowing to one is a
    /// convenience over data they can read anyway.
    /// </summary>
    [Fact]
    public void A_tenant_level_caller_may_narrow_to_one_organization()
    {
        var rendered = Render(
            organizationId: null,
            filterOrganizationId: "organization-2");

        rendered.ToString().Should().Contain("organization-2");
        rendered.ToString().Should().NotContain("$or");
    }

    [Fact]
    public void No_filter_leaves_the_query_exactly_as_it_was()
    {
        Render(organizationId: "organization-1", filterOrganizationId: null)
            .ToString()
            .Should()
            .Be(Render(organizationId: "organization-1").ToString());
    }

    private static List<string> Conditions(BsonDocument rendered) =>
        rendered.Contains("$and")
            ? [.. rendered["$and"].AsBsonArray.Select(value => value.ToString()!)]
            : [.. rendered.Elements.Select(element =>
                new BsonDocument(element.Name, element.Value).ToString()!)];

    private static BsonDocument Render(
        string? organizationId,
        string? currencyCode = null,
        string? filterOrganizationId = null)
    {
        var serializer = BsonSerializer
            .SerializerRegistry.GetSerializer<PaymentDetail>();

        return PaymentQueryRepository
            .BuildQueryFilter(new PaymentQueryCriteria
            {
                TenantId = "tenant-1",
                OrganizationId = organizationId,
                FilterOrganizationId = filterOrganizationId,
                CurrencyCode = currencyCode
            })
            .Render(new RenderArgs<PaymentDetail>(
                serializer,
                BsonSerializer.SerializerRegistry));
    }
}
