using System.Text;
using System.Text.Json;

namespace Subscription.DomainService.Reporting;

/// <summary>
/// A page boundary in the subscription roster.
/// </summary>
public sealed record SubscriptionReportCursor(DateTime CreatedAtUtc, string SubscriptionId);

/// <summary>
/// Encodes a roster page boundary as an opaque cursor, bound to the tenant it was issued for.
/// </summary>
/// <remarks>
/// The binding is the point. A cursor is a value the client holds and can edit, so one issued to
/// tenant A must be refused when tenant B presents it rather than quietly paging through their
/// subscriptions. Without that check the cursor is an access-control bypass wearing base64.
/// <para>
/// Deliberately separate from <see cref="Services.FinancialDocumentCursorCodec"/>, which does the
/// same job for document history. The two carry different keys — that one an issue date and a
/// document id bound to an organization, this one a creation date and a subscription id bound to a
/// tenant — and sharing one codec would mean a reporting cursor whose payload field was named for
/// documents and whose scope field was named for organizations while holding a tenant. The
/// duplication is about forty lines; the alternative is a type whose names lie about what is in it.
/// </para>
/// </remarks>
public static class SubscriptionReportCursorCodec
{
    private const int Version = 1;
    private const int MaximumCursorLength = 2_048;
    private const int MaximumIdLength = 200;

    public static string Encode(string tenantId, SubscriptionReportCursor boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);

        var json = JsonSerializer.Serialize(new CursorPayload
        {
            Version = Version,
            TenantId = tenantId,
            CreatedAtUtc = boundary.CreatedAtUtc,
            SubscriptionId = boundary.SubscriptionId
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(
        string? cursor,
        string tenantId,
        out SubscriptionReportCursor? boundary)
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
                payload.CreatedAtUtc == default ||
                string.IsNullOrWhiteSpace(payload.SubscriptionId) ||
                payload.SubscriptionId.Length > MaximumIdLength ||
                !string.Equals(payload.TenantId, tenantId, StringComparison.Ordinal))
            {
                return false;
            }

            boundary = new SubscriptionReportCursor(
                payload.CreatedAtUtc.ToUniversalTime(),
                payload.SubscriptionId);

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

        public string TenantId { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; }

        public string SubscriptionId { get; set; } = string.Empty;
    }
}
