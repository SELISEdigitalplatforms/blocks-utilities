using System.Reflection;
using Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Payment.DomainService.Responses;

namespace XUnitTest.Subscription;

/// <summary>
/// What the reporting controller promises in OpenAPI, checked against itself.
/// </summary>
/// <remarks>
/// These attributes are metadata: getting one wrong changes nothing at runtime and no other test
/// notices, but Swagger and Scalar publish it and a generated client deserializes against it. The
/// defect this guards actually shipped — two actions declared a sibling report's payload for their
/// 503, so a client following the contract would have parsed the wrong shape on the one response
/// it is least able to re-request.
/// </remarks>
public sealed class SubscriptionReportsControllerTests
{
    public static TheoryData<string> Actions()
    {
        var data = new TheoryData<string>();

        foreach (var action in ReportActions())
        {
            data.Add(action.Name);
        }

        return data;
    }

    /// <summary>
    /// Every declared body on one action describes that action's own payload.
    /// </summary>
    /// <remarks>
    /// Success and failure share one envelope type here — <c>ApiResponse&lt;T&gt;</c> with the same
    /// <c>T</c> — so every response that carries a body carries the same one. That makes the check
    /// a simple equality rather than a table of expected pairs, and a table would only be another
    /// place to make the same mistake.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Actions))]
    public void Every_declared_response_body_matches_the_action_it_belongs_to(string actionName)
    {
        var action = ReportActions().Single(candidate => candidate.Name == actionName);

        var bodies = action
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Where(attribute => attribute.Type != typeof(void))
            .ToList();

        bodies.Should().NotBeEmpty("every reporting action returns an envelope on success");

        var success = bodies
            .Single(attribute => attribute.StatusCode == StatusCodes.Status200OK)
            .Type;

        foreach (var body in bodies)
        {
            body.Type.Should().Be(
                success,
                "{0} declares {1} for status {2}, which is a different report's payload",
                actionName,
                body.Type.Name,
                body.StatusCode);
        }
    }

    /// <summary>
    /// Organization resolution can fail as unavailable, so every action can answer 503.
    /// </summary>
    /// <remarks>
    /// Stated once per action rather than left to the status-code table, because a caller that does
    /// not expect a 503 does not retry one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Actions))]
    public void Every_action_declares_the_unavailable_response(string actionName) =>
        ReportActions()
            .Single(candidate => candidate.Name == actionName)
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .Should().Contain(StatusCodes.Status503ServiceUnavailable);

    /// <summary>
    /// Reporting is wider than every other subscription endpoint, so it carries its own scope.
    /// </summary>
    [Theory]
    [MemberData(nameof(Actions))]
    public void Every_action_is_guarded_by_the_reporting_scope(string actionName)
    {
        var action = ReportActions().Single(candidate => candidate.Name == actionName);

        var scopes = action
            .GetCustomAttributes()
            .Where(attribute => attribute.GetType().Name.Contains(
                "ProtectedEndPoint", StringComparison.Ordinal))
            .Select(attribute => attribute.GetType()
                .GetProperties()
                .Select(property => property.GetValue(attribute) as string)
                .FirstOrDefault(value => value is not null && value.Contains("::", StringComparison.Ordinal)))
            .ToList();

        scopes.Should().ContainSingle()
            .Which.Should().Be(
                "blocks-utilities::subscription-report::read",
                "a client permitted to read its own subscription is not thereby permitted to " +
                "read everyone's");
    }

    private static IReadOnlyList<MethodInfo> ReportActions() =>
        [.. typeof(SubscriptionReportsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes<HttpGetAttribute>().Any())];
}
