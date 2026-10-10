using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// How a member's address, name and language are read from IAM for their seat email (spec 001).
/// </summary>
/// <remarks>
/// Guards against mailing the wrong person, widening the read beyond the caller's organization,
/// and an IAM hiccup turning into a failed seat assignment.
/// </remarks>
public sealed class IamMemberDirectoryTests : IDisposable
{
    private readonly Mock<IHttpService> _http = new();
    private string? _requestedUrl;
    private Dictionary<string, string>? _requestedHeaders;

    public IamMemberDirectoryTests()
    {
        BlocksContext.SetContext(BlocksContext.Create(
            "tenant-1", null, "admin-1", true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, "caller-token", null));
    }

    public void Dispose() => BlocksContext.ClearContext();

    [Fact]
    public async Task A_member_is_read_on_the_callers_own_token_without_widening_the_organization()
    {
        Reply("""{"data":{"email":" ada@example.com ","firstName":"Ada","lastName":"Lovelace","language":"en-GB"}}""");

        var contact = await Directory().FindUserAsync("user-1", CancellationToken.None);

        contact.Should().Be(new MemberContact("ada@example.com", "Ada Lovelace", "en-GB"));
        _requestedUrl.Should().Be("https://iam.example.com/api/iam/users/user-1",
            "IAM scopes by an organizationId query value instead of the token's, which would widen the read");
        _requestedHeaders!["Authorization"].Should().Be("Bearer caller-token",
            "IAM must judge this read by the administrator's own permissions");
    }

    [Fact]
    public async Task A_member_with_no_name_is_addressed_by_their_email()
    {
        Reply("""{"data":{"email":"ada@example.com","firstName":"","lastName":"","language":""}}""");

        var contact = await Directory().FindUserAsync("user-1", CancellationToken.None);

        contact.Should().Be(new MemberContact("ada@example.com", "ada@example.com", null));
    }

    [Fact]
    public async Task A_member_outside_the_callers_organization_is_not_found()
    {
        // IAM's answer for a user it will not show this caller: an empty data object, not a 404.
        Reply("""{"data":{}}""");

        var contact = await Directory().FindUserAsync("user-1", CancellationToken.None);

        contact.Should().BeNull("a person with no address cannot be mailed");
    }

    [Fact]
    public async Task An_iam_failure_reads_as_not_found_rather_than_throwing()
    {
        _http.Setup(service => service.SendRequest<It.IsAnyType>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Returns(new InvocationFunc(_ => throw new HttpRequestException("down")));

        var directory = Directory();

        var act = () => directory.FindUserAsync("user-1", CancellationToken.None);

        (await act.Should().NotThrowAsync(
            "an IAM outage must cost the member an email, never their seat")).Which.Should().BeNull();
    }

    [Fact]
    public async Task The_organization_name_comes_from_iam()
    {
        Reply("""{"organization":{"itemId":"org-1","name":" Analytical Engines Ltd "}}""");

        var name = await Directory().FindOrganizationNameAsync("org-1", CancellationToken.None);

        name.Should().Be("Analytical Engines Ltd");
        _requestedUrl.Should().Be("https://iam.example.com/api/iam/organizations/org-1");
    }

    /// <summary>
    /// Answers every typed request by deserializing <paramref name="json"/> into whatever the
    /// directory asked for, so the test exercises its real reply models.
    /// </summary>
    private void Reply(string json) =>
        _http.Setup(service => service.SendRequest<It.IsAnyType>(
                It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Returns(new InvocationFunc(invocation =>
            {
                _requestedUrl = (string)invocation.Arguments[1];
                _requestedHeaders = (Dictionary<string, string>?)invocation.Arguments[4];
                var type = invocation.Method.GetGenericArguments()[0];
                var reply = System.Text.Json.JsonSerializer.Deserialize(json, type);
                var tupleType = typeof(ValueTuple<,>).MakeGenericType(type, typeof(string));
                var tuple = Activator.CreateInstance(tupleType, reply, string.Empty);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(tupleType)
                    .Invoke(null, [tuple]);
            }));

    private IamMemberDirectory Directory()
    {
        var options = new Mock<IOptionsMonitor<PaymentOptions>>();
        options.Setup(monitor => monitor.CurrentValue)
            .Returns(new PaymentOptions { IamBaseUrl = "https://iam.example.com" });

        return new IamMemberDirectory(
            _http.Object, options.Object, NullLogger<IamMemberDirectory>.Instance);
    }
}
