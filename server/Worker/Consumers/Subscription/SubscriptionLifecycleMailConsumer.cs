using Blocks.Genesis;
using Microsoft.Extensions.DependencyInjection;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Services;

namespace Worker.Consumers.Subscription;

/// <summary>
/// Turns subscription lifecycle facts into mail requests — usage warnings, and the seat, plan and
/// cancellation notices — while leaving every event available to other consumers.
/// </summary>
/// <remarks>
/// One consumer for both, because the host registers one consumer per message type. Each service
/// ignores the events that are not its own, so offering every event to both is the whole dispatch.
/// Still bound to the queue the usage warnings always used: renaming a durable queue creates a new
/// one and strands whatever the old one held.
/// </remarks>
public sealed class SubscriptionLifecycleMailConsumer :
    IConsumer<SubscriptionLifecycleEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SubscriptionLifecycleMailConsumer(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    public async Task Consume(SubscriptionLifecycleEvent lifecycleEvent)
    {
        using var scope = _scopeFactory.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<IUsageThresholdEmailService>()
            .SendAsync(lifecycleEvent, CancellationToken.None);

        await scope.ServiceProvider
            .GetRequiredService<ISubscriptionNotificationEmailService>()
            .SendAsync(lifecycleEvent, CancellationToken.None);
    }
}
