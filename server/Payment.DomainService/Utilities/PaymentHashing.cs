using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payment.DomainService.Requests;

namespace Payment.DomainService.Utilities;

public static class PaymentHashing
{
    public static string CreateRequestHash(MakePaymentRequest request)
    {
        var canonical = new
        {
            ProviderName = NormalizeUpper(request.ProviderName),
            request.Amount,
            CurrencyCode = NormalizeUpper(request.CurrencyCode),
            OrderId = Normalize(request.OrderId),
            Description = Normalize(request.Description),
            PaymentMeansAliasId = Normalize(request.PaymentMeansAliasId),
            SavePaymentMethod = request.ShouldSavePaymentMethod,
            Language = NormalizeLower(request.Language),
            request.IsRecurring,
            RecurringModel = Normalize(request.RecurringModel),
            TransactionId = Normalize(request.TransactionId),
            CustomerName = Normalize(request.CustomerName),
            CustomerEmail = NormalizeLower(request.CustomerEmail),
            CustomerAddress = Normalize(request.CustomerAddress),
            CustomerCity = Normalize(request.CustomerCity),
            CustomerPostCode = Normalize(request.CustomerPostCode),
            CustomerCountry = NormalizeUpper(request.CustomerCountry),
            CustomerPhone = Normalize(request.CustomerPhone),
            ProductName = Normalize(request.ProductName),
            ProductCategory = Normalize(request.ProductCategory),
            ProductProfile = Normalize(request.ProductProfile),
            CustomerOrganizationId = Normalize(request.CustomerOrganizationId)
        };

        return Hash(JsonSerializer.Serialize(canonical));
    }

    public static string CreateRequestHash(
        CreateRecurringPaymentRequest request)
    {
        var canonical = new
        {
            ProviderName =
                NormalizeUpper(request.ProviderName),
            StoredPaymentMethodId =
                Normalize(request.StoredPaymentMethodId),
            request.Amount,
            CurrencyCode =
                NormalizeUpper(request.CurrencyCode),
            OrderId = Normalize(request.OrderId),
            RecurringProcessingModel =
                Normalize(request.RecurringProcessingModel),
            Description = Normalize(request.Description)
        };

        return Hash(JsonSerializer.Serialize(canonical));
    }

    /// <summary>
    /// What makes one card-collection request the same as another under a reused key.
    /// </summary>
    /// <remarks>
    /// No amount, because there is none. What is left is who is collecting, for which subscriber,
    /// in which currency and against which order — change any of those and it is a different
    /// request wearing the same key, which is exactly what this exists to catch.
    /// </remarks>
    public static string CreateRequestHash(
        CreatePaymentMethodSetupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = new
        {
            ProviderName = NormalizeUpper(request.ProviderName),
            CurrencyCode = NormalizeUpper(request.CurrencyCode),
            OrderId = Normalize(request.OrderId),
            Description = Normalize(request.Description),
            CustomerEmail = NormalizeLower(request.CustomerEmail),
            CustomerOrganizationId = Normalize(request.CustomerOrganizationId),
            OrganizationId = Normalize(request.OrganizationId)
        };

        return Hash(JsonSerializer.Serialize(canonical));
    }

    public static string CreateRefundRequestHash(
        string paymentDetailId,
        CreatePaymentRefundRequest request)
    {
        var canonical = new
        {
            PaymentDetailId =
                Normalize(paymentDetailId),
            request.Amount,
            Reason = Normalize(request.Reason)
        };

        return Hash(JsonSerializer.Serialize(canonical));
    }

    public static string CreateCaptureRequestHash(
        string paymentDetailId,
        CreatePaymentCaptureRequest request)
    {
        var canonical = new
        {
            PaymentDetailId = Normalize(paymentDetailId),
            request.Amount
        };

        return Hash(JsonSerializer.Serialize(canonical));
    }

    public static string CreateLockResource(string tenantId, string idempotencyKey) =>
        Hash($"{tenantId}:{idempotencyKey}")[..32];

    public static string HashSensitiveValue(string value) => Hash(value);

    public static bool RequestHashesMatch(string existingHash, string requestHash)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(existingHash),
                Convert.FromHexString(requestHash));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeUpper(string? value) => Normalize(value)?.ToUpperInvariant();
    private static string? NormalizeLower(string? value) => Normalize(value)?.ToLowerInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
