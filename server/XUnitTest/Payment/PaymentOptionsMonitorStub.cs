using Microsoft.Extensions.Options;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>Fixed payment options, for the services that read them on every pass.</summary>
internal sealed class PaymentOptionsMonitorStub : IOptionsMonitor<PaymentOptions>
{
    public PaymentOptionsMonitorStub(PaymentOptions value) => CurrentValue = value;

    public PaymentOptions CurrentValue { get; }

    public PaymentOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<PaymentOptions, string?> listener) => null;
}
