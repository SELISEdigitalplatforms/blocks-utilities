using Microsoft.Extensions.Options;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>Fixed subscription options, for the services that read them on every pass.</summary>
internal sealed class SubscriptionOptionsMonitorStub : IOptionsMonitor<SubscriptionOptions>
{
    public SubscriptionOptionsMonitorStub(SubscriptionOptions value) => CurrentValue = value;

    public SubscriptionOptions CurrentValue { get; }

    public SubscriptionOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
}
