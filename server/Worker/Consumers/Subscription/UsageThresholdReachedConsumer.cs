using Blocks.Genesis;
using Microsoft.Extensions.DependencyInjection;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Services;

namespace Worker.Consumers.Subscription;

/// <summary>
/// Turns subscription threshold facts into mail requests while leaving all other lifecycle
/// events available to their own consumers.
/// </summary>
public sealed class UsageThresholdReachedConsumer :
    IConsumer<SubscriptionLifecycleEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public UsageThresholdReachedConsumer(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    public async Task Consume(SubscriptionLifecycleEvent lifecycleEvent)
    {
        using var scope = _scopeFactory.CreateScope();
        var emails = scope.ServiceProvider.GetRequiredService<
            IUsageThresholdEmailService>();

        await emails.SendAsync(lifecycleEvent, CancellationToken.None);
    }
}
