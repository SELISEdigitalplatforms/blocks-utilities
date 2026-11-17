using Blocks.Genesis;
using Microsoft.AspNetCore.Http;

namespace Payment.DomainService.Utilities;

/// <summary>
/// The caller's own sign-in token, for forwarding to IAM on their behalf.
/// </summary>
public interface ICallerToken
{
    /// <summary>The raw token, without a <c>Bearer</c> prefix, or null when there is no caller.</summary>
    string? Current();
}

/// <summary>
/// Reads the token from the request itself — the <c>Authorization</c> header, or the tenant's sign-in
/// cookie the console authenticates with — the same way Genesis found it to authenticate the request.
/// </summary>
/// <remarks>
/// Not <see cref="BlocksContext.OAuthToken"/> alone: Genesis 4.5.5 builds every request's context
/// with that field empty, so a lookup that read only it never reached IAM. Organization verification,
/// a seat member's email, and the organization and buyer names recorded at signup all failed that way,
/// silently, in production. The context's value is still the fallback, in case a later Genesis fills it.
/// <para>
/// A third-party token is not forwarded: it was issued by the tenant's own identity provider, not by
/// IAM, so IAM would refuse it. With no request at all — the worker — there is no caller, and null is
/// the honest answer.
/// </para>
/// </remarks>
public sealed class CallerToken : ICallerToken
{
    private readonly IHttpContextAccessor _accessor;
    private readonly ITenants _tenants;

    public CallerToken(IHttpContextAccessor accessor, ITenants tenants)
    {
        _accessor = accessor;
        _tenants = tenants;
    }

    public string? Current()
    {
        if (_accessor.HttpContext?.Request is { } request)
        {
            var (token, isThirdParty) = TokenHelper.GetToken(request, _tenants);

            if (!isThirdParty && !string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        return BlocksContext.GetContext()?.OAuthToken is { Length: > 0 } fromContext
            ? fromContext
            : null;
    }
}
