using System.Text;
using System.Text.Json;

namespace Subscription.DomainService.Services;

/// <summary>
/// Encodes a usage-report page boundary as an opaque cursor, bound to the tenant and the whole
/// filter set the page was issued under.
/// </summary>
/// <remarks>
/// Modelled on <see cref="FinancialDocumentCursorCodec"/> — see its own remarks on why an
/// unchecked cursor is "an access-control bypass with a base64 costume" — but bound to more than
/// an organization. These endpoints are tenant-scoped-with-optional-organization-filter rather
/// than purely organization-scoped, so a cursor also has to be refused if presented against a
/// different organization filter, meter, subscription, granularity or date range than the one it
/// was issued under: any of those changes what "the next page" means, and honouring a stale
/// cursor against a new filter would silently splice two different result sets together.
/// </remarks>
public static class UsageReportCursorCodec
{
    private const int Version = 1;
    private const int MaximumCursorLength = 4_096;

    public static string Encode(UsageReportCursorScope scope, string boundary)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var json = JsonSerializer.Serialize(new CursorPayload
        {
            Version = Version,
            TenantId = scope.TenantId,
            OrganizationId = scope.OrganizationId,
            SubscriptionId = scope.SubscriptionId,
            MeterKey = scope.MeterKey,
            Granularity = scope.Granularity,
            FromUtc = scope.FromUtc,
            ToUtc = scope.ToUtc,
            Boundary = boundary
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(
        string? cursor,
        UsageReportCursorScope scope,
        out string boundary)
    {
        boundary = string.Empty;

        ArgumentNullException.ThrowIfNull(scope);

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
                string.IsNullOrWhiteSpace(payload.Boundary) ||
                !string.Equals(payload.TenantId, scope.TenantId, StringComparison.Ordinal) ||
                !string.Equals(
                    payload.OrganizationId ?? string.Empty,
                    scope.OrganizationId ?? string.Empty,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    payload.SubscriptionId ?? string.Empty,
                    scope.SubscriptionId ?? string.Empty,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    payload.MeterKey ?? string.Empty,
                    scope.MeterKey ?? string.Empty,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    payload.Granularity ?? string.Empty,
                    scope.Granularity ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase) ||
                payload.FromUtc != scope.FromUtc ||
                payload.ToUtc != scope.ToUtc)
            {
                return false;
            }

            boundary = payload.Boundary;

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

        public string? OrganizationId { get; set; }

        public string? SubscriptionId { get; set; }

        public string? MeterKey { get; set; }

        public string? Granularity { get; set; }

        public DateTime? FromUtc { get; set; }

        public DateTime? ToUtc { get; set; }

        public string Boundary { get; set; } = string.Empty;
    }
}

/// <summary>The filter set a usage-report cursor is bound to.</summary>
public sealed record UsageReportCursorScope(
    string TenantId,
    string? OrganizationId,
    string? SubscriptionId,
    string? MeterKey,
    string? Granularity,
    DateTime? FromUtc,
    DateTime? ToUtc);
