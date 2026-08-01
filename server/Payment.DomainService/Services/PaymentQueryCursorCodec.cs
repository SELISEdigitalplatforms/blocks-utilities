using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;

namespace Payment.DomainService.Services;

public sealed class PaymentQueryCursorCodec :
    IPaymentQueryCursorCodec
{
    private const int CursorVersion = 1;
    private const int MaximumCursorLength = 4_096;
    private const int MaximumBoundaryLength = 256;
    private const int MaximumPaymentIdLength = 128;

    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    public string Encode(
        PaymentQueryCriteria criteria,
        PaymentQueryRecord record)
    {
        var payload = new PaymentQueryCursorPayload
        {
            Version = CursorVersion,
            SortBy = criteria.SortBy,
            SortDirection = criteria.SortDirection,
            BoundaryValue = GetBoundaryValue(criteria.SortBy, record),
            PaymentDetailId = record.PaymentDetailId,
            FilterFingerprint = CreateFilterFingerprint(criteria)
        };
        var json = JsonSerializer.Serialize(
            payload,
            SerializerOptions);

        return EncodeBase64Url(Encoding.UTF8.GetBytes(json));
    }

    public bool TryDecode(
        string cursor,
        PaymentQueryCriteria criteria,
        out PaymentQueryCursorBoundary? boundary)
    {
        boundary = null;

        if (string.IsNullOrWhiteSpace(cursor) ||
            cursor.Length > MaximumCursorLength ||
            !TryDecodeBase64Url(cursor, out var bytes))
        {
            return false;
        }

        PaymentQueryCursorPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<PaymentQueryCursorPayload>(
                bytes,
                SerializerOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload == null ||
            payload.Version != CursorVersion ||
            payload.BoundaryValue == null ||
            payload.BoundaryValue.Length > MaximumBoundaryLength ||
            string.IsNullOrWhiteSpace(payload.PaymentDetailId) ||
            payload.PaymentDetailId.Length > MaximumPaymentIdLength ||
            !string.Equals(
                payload.SortBy,
                criteria.SortBy,
                StringComparison.Ordinal) ||
            !string.Equals(
                payload.SortDirection,
                criteria.SortDirection,
                StringComparison.Ordinal) ||
            !string.Equals(
                payload.FilterFingerprint,
                CreateFilterFingerprint(criteria),
                StringComparison.Ordinal))
        {
            return false;
        }

        return TryCreateBoundary(
            payload,
            criteria.SortBy,
            out boundary);
    }

    private static bool TryCreateBoundary(
        PaymentQueryCursorPayload payload,
        string sortBy,
        out PaymentQueryCursorBoundary? boundary)
    {
        boundary = null;

        if (sortBy == PaymentQuerySortFields.Amount)
        {
            if (!decimal.TryParse(
                    payload.BoundaryValue,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                return false;
            }

            boundary = new PaymentQueryCursorBoundary(
                payload.PaymentDetailId,
                null,
                amount,
                null);

            return true;
        }

        if (sortBy == PaymentQuerySortFields.PaymentDate)
        {
            if (!DateTime.TryParse(
                    payload.BoundaryValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var paymentDate))
            {
                return false;
            }

            boundary = new PaymentQueryCursorBoundary(
                payload.PaymentDetailId,
                null,
                null,
                paymentDate.ToUniversalTime());

            return true;
        }

        if (sortBy is PaymentQuerySortFields.ProviderName or
            PaymentQuerySortFields.PaymentStatus)
        {
            boundary = new PaymentQueryCursorBoundary(
                payload.PaymentDetailId,
                payload.BoundaryValue,
                null,
                null);

            return true;
        }

        return false;
    }

    private static string GetBoundaryValue(
        string sortBy,
        PaymentQueryRecord record) =>
        sortBy switch
        {
            PaymentQuerySortFields.ProviderName => record.ProviderName,
            PaymentQuerySortFields.Amount => record.Amount.ToString(
                "G29",
                CultureInfo.InvariantCulture),
            PaymentQuerySortFields.PaymentDate =>
                record.PaymentDateUtc.ToUniversalTime().ToString(
                    "O",
                    CultureInfo.InvariantCulture),
            PaymentQuerySortFields.PaymentStatus => record.PaymentStatus,
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortBy),
                sortBy,
                "Unsupported payment sort field.")
        };

    private static string CreateFilterFingerprint(
        PaymentQueryCriteria criteria)
    {
        var canonical = JsonSerializer.Serialize(
            new
            {
                ProviderNames = criteria.ProviderNames
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                PaymentStatuses = criteria.PaymentStatuses
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                MinAmount = criteria.MinAmount?.ToString(
                    "G29",
                    CultureInfo.InvariantCulture),
                MaxAmount = criteria.MaxAmount?.ToString(
                    "G29",
                    CultureInfo.InvariantCulture),
                PaymentDateFromUtc = criteria.PaymentDateFromUtc?.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                PaymentDateToUtc = criteria.PaymentDateToUtc?.ToString(
                    "O",
                    CultureInfo.InvariantCulture),
                criteria.CurrencyCode,
                criteria.OrderId,
                criteria.PaymentDetailId,
                criteria.PaymentFlow
            });

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical)));
    }

    private static string EncodeBase64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryDecodeBase64Url(
        string value,
        out byte[] bytes)
    {
        bytes = [];

        if (value.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not '-' and not '_'))
        {
            return false;
        }

        var padded = value
            .Replace('-', '+')
            .Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => "invalid"
        };

        try
        {
            bytes = Convert.FromBase64String(padded);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
