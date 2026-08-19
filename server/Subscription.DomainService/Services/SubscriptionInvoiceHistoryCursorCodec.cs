using System.Text;
using System.Text.Json;
using Subscription.DomainService.Repositories;

namespace Subscription.DomainService.Services;

public static class SubscriptionInvoiceHistoryCursorCodec
{
    private const int Version = 1;
    private const int MaximumCursorLength = 2_048;
    private const int MaximumIdLength = 200;

    public static string Encode(
        string organizationId,
        SubscriptionInvoiceHistoryRecord record)
    {
        var json = JsonSerializer.Serialize(new CursorPayload
        {
            Version = Version,
            OrganizationId = organizationId,
            IssuedAtUtc = record.IssuedAtUtc,
            PaymentDetailId = record.PaymentDetailId
        });

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(
        string? cursor,
        string organizationId,
        out SubscriptionInvoiceHistoryCursor? boundary)
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
                normalized.Length + ((4 - normalized.Length % 4) % 4),
                '=');
            var payload = JsonSerializer.Deserialize<CursorPayload>(
                Convert.FromBase64String(padded));

            if (payload is null ||
                payload.Version != Version ||
                payload.IssuedAtUtc == default ||
                string.IsNullOrWhiteSpace(payload.PaymentDetailId) ||
                payload.PaymentDetailId.Length > MaximumIdLength ||
                !string.Equals(
                    payload.OrganizationId,
                    organizationId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            boundary = new SubscriptionInvoiceHistoryCursor(
                payload.IssuedAtUtc.ToUniversalTime(),
                payload.PaymentDetailId);
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

        public string PaymentDetailId { get; set; } = string.Empty;
    }
}
