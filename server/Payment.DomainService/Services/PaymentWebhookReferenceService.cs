using Payment.DomainService.Utilities;

namespace Payment.DomainService.Services;

public sealed class PaymentWebhookReferenceService : IPaymentWebhookReferenceService
{
    private const string Version = "p1";
    private const int MaximumProviderReferenceLength = 80;

    public bool TryCreate(
        string tenantId,
        string paymentDetailId,
        out string reference)
    {
        reference = string.Empty;

        if (!TenantRoutingToken.TryEncode(tenantId, out var tenantToken) ||
            !Guid.TryParse(paymentDetailId, out _))
        {
            return false;
        }

        var candidate = $"{Version}.{tenantToken}.{paymentDetailId}";
        if (candidate.Length > MaximumProviderReferenceLength)
        {
            return false;
        }

        reference = candidate;
        return true;
    }

    public bool TryParse(
        string? reference,
        out PaymentWebhookRoute route)
    {
        route = new PaymentWebhookRoute(string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(reference) ||
            reference.Length > MaximumProviderReferenceLength)
        {
            return false;
        }

        var parts = reference.Split('.', 3, StringSplitOptions.None);
        if (parts.Length != 3 ||
            !string.Equals(parts[0], Version, StringComparison.Ordinal) ||
            !TenantRoutingToken.TryDecode(parts[1], out var tenantId) ||
            !Guid.TryParse(parts[2], out _))
        {
            return false;
        }

        route = new PaymentWebhookRoute(tenantId, parts[2]);
        return true;
    }
}
