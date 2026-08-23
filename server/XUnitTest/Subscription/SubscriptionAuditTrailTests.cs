using Microsoft.Extensions.Logging;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

public sealed class SubscriptionAuditTrailTests
{
    [Fact]
    public async Task Record_persists_the_event_and_defaults_operation_id_to_correlation_id()
    {
        var repository = new Mock<ISubscriptionAuditRepository>();
        var trail = new SubscriptionAuditTrail(
            repository.Object,
            Mock.Of<ILogger<SubscriptionAuditTrail>>());
        var auditEvent = Event();

        await trail.RecordAsync(auditEvent, CancellationToken.None);

        Assert.Equal("correlation-1", auditEvent.OperationId);
        repository.Verify(x => x.AppendAsync(auditEvent, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Audit_storage_failure_never_turns_a_completed_charge_into_a_retry()
    {
        var repository = new Mock<ISubscriptionAuditRepository>();
        repository.Setup(x => x.AppendAsync(
                It.IsAny<SubscriptionAuditEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit unavailable"));
        var trail = new SubscriptionAuditTrail(
            repository.Object,
            Mock.Of<ILogger<SubscriptionAuditTrail>>());

        var exception = await Record.ExceptionAsync(() =>
            trail.RecordAsync(Event(), CancellationToken.None));

        Assert.Null(exception);
    }

    private static SubscriptionAuditEvent Event() => new()
    {
        TenantId = "tenant-1",
        OrganizationId = "organization-1",
        SubscriptionId = "subscription-1",
        CorrelationId = "correlation-1",
        Operation = "Renewal",
        Stage = "ChargeCompleted",
        Outcome = "Succeeded",
        Source = "Worker"
    };
}
