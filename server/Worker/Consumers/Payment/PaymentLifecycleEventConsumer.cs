using Blocks.Genesis;
using Payment.DomainService.Entities;

namespace Worker.Consumers.Payment;

/// <summary>
/// Acknowledges this service's own payment lifecycle events and does nothing else with them.
/// </summary>
/// <remarks>
/// The payment lifecycle topic is listed in the worker's message configuration because that list is
/// also what creates the topic the payment outbox publishes to. Listing it subscribes the worker as
/// well, and with no consumer registered every published payment event was logged as
/// "No consumer found for message type PaymentLifecycleEvent" at Error. The events are for other
/// services; inside utilities the webhook work command already drives everything that follows a
/// payment, so there is nothing for this consumer to do.
/// </remarks>
public sealed class PaymentLifecycleEventConsumer : IConsumer<PaymentLifecycleEvent>
{
    public Task Consume(PaymentLifecycleEvent context) => Task.CompletedTask;
}
