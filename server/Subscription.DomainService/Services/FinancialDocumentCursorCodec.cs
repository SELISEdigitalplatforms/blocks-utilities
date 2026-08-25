using System.Text;
using System.Text.Json;
using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Services;

/// <summary>
/// Encodes a document-history page boundary as an opaque cursor.
/// </summary>
/// <remarks>
/// Opaque, versioned and bound to the organization it was issued for. The last of those is the one
/// that matters: a cursor is a value a client holds and can edit, so one issued for organization A
/// must be refused when presented by organization B rather than quietly paging through their
/// documents. Without that check the cursor becomes an access-control bypass with a base64 costume.
/// </remarks>
public static class FinancialDocumentCursorCodec
{
    private const int Version = 1;
    private const int MaximumCursorLength = 2_048;
    private const int MaximumIdLength = 200;

    public static string Encode(string organizationId, FinancialDocumentCursor boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);

        var json = JsonSerializer.Serialize(new CursorPayload
        {
            Version = Version,
            OrganizationId = organizationId,
            IssuedAtUtc = boundary.IssuedAtUtc,
            DocumentId = boundary.DocumentId
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(
        string? cursor,
        string organizationId,
        out FinancialDocumentCursor? boundary)
    {
        boundary = null;

        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > MaximumCursorLength)
        {
            return false;
        }

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            var padded = normalized.PadRight(
                normalized.Length + ((4 - (normalized.Length % 4)) % 4),
                '=');

            var payload = JsonSerializer.Deserialize<CursorPayload>(
                Convert.FromBase64String(padded));

            if (payload is null ||
                payload.Version != Version ||
                payload.IssuedAtUtc == default ||
                string.IsNullOrWhiteSpace(payload.DocumentId) ||
                payload.DocumentId.Length > MaximumIdLength ||
                !string.Equals(
                    payload.OrganizationId,
                    organizationId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            boundary = new FinancialDocumentCursor(
                payload.IssuedAtUtc.ToUniversalTime(),
                payload.DocumentId);

            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private sealed class CursorPayload
    {
        public int Version { get; set; }

        public string OrganizationId { get; set; } = string.Empty;

        public DateTime IssuedAtUtc { get; set; }

        public string DocumentId { get; set; } = string.Empty;
    }
}
