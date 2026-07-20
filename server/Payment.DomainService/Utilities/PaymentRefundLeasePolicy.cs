namespace Payment.DomainService.Utilities;

public static class PaymentRefundLeasePolicy
{
    public static TimeSpan Resolve(PaymentOptions options)
    {
        var configuredLeaseSeconds = Math.Clamp(
            options.ProcessingLeaseSeconds,
            10,
            120);
        var providerCallSeconds = Math.Clamp(
            options.ProviderTimeoutSeconds,
            1,
            60);

        return TimeSpan.FromSeconds(
            Math.Max(
                configuredLeaseSeconds,
                providerCallSeconds + 10));
    }
}
