using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Finding the caller's own sign-in token, to forward to IAM on their behalf.
/// </summary>
/// <remarks>
/// Guards the production failure behind INV-2026-000023 "Initiated by" a bare id: Genesis 4.5.5
/// leaves <see cref="BlocksContext.OAuthToken"/> empty on every request, so IAM lookups that read only
/// it never ran — no buyer name, no organization name, no seat email, and nothing logged to say so.
/// </remarks>
public sealed class CallerTokenTests : IDisposable
{
    private readonly Mock<ITenants> _tenants = new();

    public CallerTokenTests() => Context(oauthToken: null);

    public void Dispose() => BlocksContext.ClearContext();

    [Fact]
    public void A_bearer_header_is_the_callers_token_even_though_the_context_carries_none()
    {
        var request = Request();
        request.Headers.Authorization = "Bearer header-token";

        Token(request).Should().Be("header-token",
            because: "Genesis authenticated the request with this token and then left it off the context");
    }

    [Fact]
    public void The_consoles_sign_in_cookie_is_the_callers_token()
    {
        var request = Request();
        request.Headers.Origin = "https://utilities.seliseblocks.com";
        request.Headers.Cookie = "utilities.seliseblocks.com=cookie-token";

        Token(request).Should().Be("cookie-token",
            because: "the console signs in with a cookie named for its own domain, not a header");
    }

    [Fact]
    public void A_third_party_token_is_not_forwarded_to_iam()
    {
        _tenants.Setup(tenants => tenants.GetTenantByID("tenant-1")).Returns(new Tenant
        {
            DbConnectionString = string.Empty,
            JwtTokenParameters = new JwtTokenParameters
            {
                PrivateCertificatePassword = string.Empty,
                IssueDate = DateTime.UtcNow
            },
            ThirdPartyJwtTokenParameters = new ThirdPartyJwtTokenParameters { CookieKey = "partner" }
        });
        var request = Request();
        request.Headers.Origin = "https://partner.example";
        request.Headers.Cookie = "partner=partner-token";

        Token(request).Should().BeNull(
            because: "IAM did not issue it and would refuse it, so forwarding it only leaks it");
    }

    [Fact]
    public void Without_a_request_the_context_token_is_still_used()
    {
        Context(oauthToken: "context-token");

        new CallerToken(new HttpContextAccessor(), _tenants.Object).Current().Should().Be("context-token");
    }

    [Fact]
    public void The_worker_has_no_caller()
    {
        new CallerToken(new HttpContextAccessor(), _tenants.Object).Current().Should().BeNull(
            because: "a lookup with no caller must skip rather than borrow somebody's authority");
    }

    private string? Token(HttpRequest request) =>
        new CallerToken(new HttpContextAccessor { HttpContext = request.HttpContext }, _tenants.Object).Current();

    private static HttpRequest Request() => new DefaultHttpContext().Request;

    private static void Context(string? oauthToken) =>
        BlocksContext.SetContext(BlocksContext.Create(
            "tenant-1", null, "user-1", true, null, "org-1",
            DateTime.UtcNow.AddHours(1), null, null, null, null, null, oauthToken, null));
}
