using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Utility.DomainService.Drive;
using Utility.DomainService.Drive.Service;

namespace XUnitTest.Drive;

/// <summary>
/// Guards how Graph throttling and paging cursors are handled. The failures these catch are a
/// signature app that hammers a throttled tenant into a longer ban, and a cursor that makes the
/// API send a user's bearer token to a host an attacker chose.
/// </summary>
public sealed class GraphClientTests
{
    private readonly ScriptedHttpHandler _http = new();
    private readonly DriveMetrics _metrics = new();
    private readonly GraphClient _client;

    public GraphClientTests()
    {
        _client = new GraphClient(
            DriveTestSupport.Factory(_http),
            DriveTestSupport.Options(),
            _metrics,
            DriveTestSupport.Clock(),
            NullLogger<GraphClient>.Instance);
    }

    [Fact]
    public async Task AC17_two_throttled_answers_then_success_is_a_success_after_three_calls()
    {
        _http.Sequence(HttpMethod.Get, "/me/drive",
            ScriptedHttpHandler.Throttled(1),
            ScriptedHttpHandler.Throttled(1),
            ScriptedHttpHandler.Json(HttpStatusCode.OK, "{\"id\":\"drive-1\"}"));

        var result = await _client.SendAsync("test", "token", () => new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _http.Requests.Should().HaveCount(3, because: "each 429 is retried once after its Retry-After");
        _http.Requests.Should().AllSatisfy(r => r.Message.Headers.Authorization!.Parameter.Should().Be("token"));
        result.Value!.Dispose();
    }

    [Fact]
    public async Task AC18_throttled_past_the_budget_fails_with_throttled_and_the_retry_hint()
    {
        _http.On(HttpMethod.Get, "/me/drive", _ => ScriptedHttpHandler.Throttled(1));

        var result = await _client.SendAsync("test", "token", () => new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/drive"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(DriveFailureKind.Throttled);
        result.RetryAfterSeconds.Should().Be(1);
        _http.Requests.Should().HaveCount(4, because: "MaxRetries is 3, so one attempt plus three retries");
    }

    [Fact]
    public async Task A_404_maps_to_item_not_found_and_a_name_clash_to_name_conflict()
    {
        _http.On(HttpMethod.Get, "/missing", HttpStatusCode.NotFound, "{\"error\":{\"code\":\"itemNotFound\",\"message\":\"gone\"}}");
        _http.On(HttpMethod.Post, "/children", HttpStatusCode.Conflict, "{\"error\":{\"code\":\"nameAlreadyExists\",\"message\":\"exists\"}}");

        var missing = await _client.SendAsync("t", "token", () => new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/missing"), CancellationToken.None);
        var clash = await _client.SendAsync("t", "token", () => new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/children"), CancellationToken.None);

        missing.FailureKind.Should().Be(DriveFailureKind.ItemNotFound);
        clash.FailureKind.Should().Be(DriveFailureKind.NameConflict);
        clash.ErrorMessage.Should().Be("exists");
    }

    [Fact]
    public void A_cursor_round_trips_a_graph_next_link()
    {
        const string next = "https://graph.microsoft.com/v1.0/drives/d/root/children?$skiptoken=abc";

        var cursor = _client.EncodeCursor(next);

        cursor.Should().NotContain("/", because: "base64url keeps the cursor safe in a query string");
        _client.DecodeCursor(cursor!).Should().Be(next);
    }

    [Fact]
    public void AC16_a_cursor_pointing_off_graph_is_rejected()
    {
        var foreign = _client.EncodeCursor("https://evil.example/collect?$skiptoken=abc")!;

        _client.DecodeCursor(foreign).Should().BeNull(
            because: "following it would send the user's bearer token to whoever minted the cursor");
        _client.DecodeCursor("not-base64!!").Should().BeNull();
    }
}
