using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.DomainService.Commands;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

public sealed class PaymentWorkDispatcherTests
{
    [Fact]
    public async Task Dispatch_sends_a_tenant_scoped_work_command()
    {
        const string tenantId = "tenant-a";
        var scheduledAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);
        ConsumerMessage<ProcessPaymentWorkCommand>? sentMessage = null;
        string? contextTenant = null;
        var messageClient = new Mock<IMessageClient>();
        messageClient.Setup(client => client.SendToMassConsumerAsync(
                It.IsAny<ConsumerMessage<ProcessPaymentWorkCommand>>()))
            .Callback<ConsumerMessage<ProcessPaymentWorkCommand>>(message =>
            {
                sentMessage = message;
                contextTenant = BlocksContext.GetContext()?.TenantId;
            })
            .Returns(Task.CompletedTask);
        var dispatcher = new PaymentWorkDispatcher(
            messageClient.Object,
            new PaymentTenantContextScopeFactory(),
            Mock.Of<ILogger<PaymentWorkDispatcher>>());

        await dispatcher.DispatchAsync(
            tenantId,
            includeRecovery: true,
            scheduledAtUtc,
            CancellationToken.None);

        sentMessage.Should().NotBeNull();
        sentMessage!.ConsumerName.Should().Be(PaymentConstants.PaymentWorkQueue);
        sentMessage.Payload.TenantId.Should().Be(tenantId);
        sentMessage.Payload.IncludeRecovery.Should().BeTrue();
        sentMessage.ScheduledEnqueueTimeUtc.Should().Be(scheduledAtUtc);
        contextTenant.Should().Be(tenantId);
    }

    [Fact]
    public async Task TryDispatch_returns_false_when_the_broker_is_unavailable()
    {
        var messageClient = new Mock<IMessageClient>();
        messageClient.Setup(client => client.SendToMassConsumerAsync(
                It.IsAny<ConsumerMessage<ProcessPaymentWorkCommand>>()))
            .ThrowsAsync(new InvalidOperationException("broker unavailable"));
        var dispatcher = new PaymentWorkDispatcher(
            messageClient.Object,
            new PaymentTenantContextScopeFactory(),
            Mock.Of<ILogger<PaymentWorkDispatcher>>());

        var dispatched = await dispatcher.TryDispatchAsync(
            "tenant-a",
            includeRecovery: false,
            cancellationToken: CancellationToken.None);

        dispatched.Should().BeFalse();
    }
}
