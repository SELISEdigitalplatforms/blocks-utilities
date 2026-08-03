namespace Payment.DomainService.Utilities;

public static class PaymentCaptureLeasePolicy
{
    public static TimeSpan Resolve(PaymentOptions options) =>
        TimeSpan.FromSeconds(
            Math.Clamp(options.ProcessingLeaseSeconds, 10, 120));
}
