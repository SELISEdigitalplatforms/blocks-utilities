using System.Text;
using System.Text.Json;

namespace Payment.DomainService.Utilities;

/// <summary>
/// The calling application's own identifier, read from the bearer token for callers the
/// platform context names as nobody.
/// </summary>
/// <remarks>
/// A client-credentials caller authenticates as an application rather than a person, so the
/// context carries no user id and no email and the resolver has nothing to record as the actor.
/// The identifier does exist — it is in the token, as <c>client_id</c> — but the platform
/// context exposes no property for it, so the only way to reach it is to read the token the
/// request already arrived with.
/// <para>
/// Read for identification only, never for authorisation. The token has already been validated
/// by the authentication middleware before any of this runs — a request that got this far
/// carries a signature the platform accepted — and nothing here re-checks it or would be
/// entitled to. What this decides is what to write in an audit record's actor field; what a
/// caller is allowed to do is still settled entirely by the tenant, the organization and the
/// endpoint's own permission.
/// </para>
/// <para>
/// Every failure is silent and returns null. A token this cannot read is not a reason to refuse
/// a request that authentication already accepted, and the caller is then simply left without an
/// actor exactly as it was before.
/// </para>
/// </remarks>
public static class PaymentTokenActor
{
    /// <summary>
    /// Marks the identifier as an application rather than a person.
    /// </summary>
    /// <remarks>
    /// A bare client id is a GUID, and a GUID in a field named for an actor is indistinguishable
    /// from a user id when somebody reads the audit trail a year later. The same reasoning keeps
    /// the email fallback out of <c>UserId</c>: a fallback should not be able to pass itself off
    /// as the thing it stood in for.
    /// </remarks>
    public const string ClientPrefix = "client:";

    // Long enough for any access token worth parsing, short enough that a caller cannot make
    // this service decode something absurd on every request.
    private const int MaximumTokenLength = 8 * 1024;

    private const int MaximumIdentifierLength = 128;

    /// <summary>
    /// The calling application's identifier, prefixed by <see cref="ClientPrefix"/>, or null
    /// when the token carries none or cannot be read.
    /// </summary>
    public static string? ClientId(string? oauthToken)
    {
        var identifier = ReadIdentifier(oauthToken);

        return identifier is null ? null : ClientPrefix + identifier;
    }

    private static string? ReadIdentifier(string? oauthToken)
    {
        if (string.IsNullOrWhiteSpace(oauthToken) || oauthToken.Length > MaximumTokenLength)
        {
            return null;
        }

        try
        {
            var payload = Payload(oauthToken);

            if (payload is null)
            {
                return null;
            }

            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // The dedicated claim first. Falling back to the subject costs nothing and covers an
            // issuer that names the application only there — the two carry the same identifier,
            // the subject merely wears an issuer namespace in front of it.
            return Identifier(document.RootElement, "client_id")
                ?? Subject(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? Payload(string oauthToken)
    {
        var token = oauthToken.Trim();

        // Whether the scheme travels with the token is the caller's business, not ours.
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token["Bearer ".Length..].Trim();
        }

        var segments = token.Split('.');

        // Three segments is a signed token, whose payload is readable. Five is an encrypted one,
        // whose payload is not — and guessing at it is worse than having no actor.
        if (segments.Length != 3)
        {
            return null;
        }

        var bytes = FromBase64Url(segments[1]);

        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private static byte[]? FromBase64Url(string segment)
    {
        if (segment.Length == 0)
        {
            return null;
        }

        var padded = new StringBuilder(segment.Length + 3);

        foreach (var character in segment)
        {
            padded.Append(character switch
            {
                '-' => '+',
                '_' => '/',
                _ => character
            });
        }

        // Base64url drops the padding that Convert.FromBase64String requires back.
        padded.Append('=', (4 - (segment.Length % 4)) % 4);

        return Convert.FromBase64String(padded.ToString());
    }

    private static string? Subject(JsonElement payload)
    {
        var subject = Identifier(payload, "sub");

        if (subject is null)
        {
            return null;
        }

        // Issuers namespace the subject — "blocks|<id>" — and the namespace is the issuer's own
        // bookkeeping rather than part of the identifier.
        var separator = subject.LastIndexOf('|');

        if (separator < 0)
        {
            return subject;
        }

        var identifier = subject[(separator + 1)..].Trim();

        return identifier.Length == 0 ? null : identifier;
    }

    private static string? Identifier(JsonElement payload, string claim)
    {
        if (!payload.TryGetProperty(claim, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString()?.Trim();

        if (string.IsNullOrEmpty(value) || value.Length > MaximumIdentifierLength)
        {
            return null;
        }

        // This is written to audit records and to logs, and it came off the wire. A control
        // character in it would let a token forge whole log entries.
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return null;
            }
        }

        return value;
    }
}
