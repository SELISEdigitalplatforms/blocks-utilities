using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// The payment audit trail: who acted is taken from the caller, never from the request; a write
/// that fails never fails the payment it describes; and an organization reads only its own events.
/// </summary>
public sealed class PaymentAuditTrailTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<IPaymentAuditRepository> _repository = new();
    private readonly Mock<IPaymentExecutionContextResolver> _context = new();
    private readonly List<PaymentAuditEvent> _written = [];

    public PaymentAuditTrailTests()
    {
        _repository
            .Setup(repository => repository.AppendAsync(It.IsAny<PaymentAuditEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentAuditEvent, CancellationToken>((auditEvent, _) => _written.Add(auditEvent))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task An_action_is_recorded_under_its_caller_and_its_subjects_organization()
    {
        Caller("org-console");

        await Trail().RecordAsync(
            new PaymentAuditEntry("PaymentRefund", "Payment", "pay-1", "Succeeded",
                OrganizationId: "org-customer", Amount: 12.5m, CurrencyCode: "CHF"),
            "corr-1",
            CancellationToken.None);

        var recorded = _written.Single();
        recorded.TenantId.Should().Be(TenantId);
        recorded.OrganizationId.Should().Be("org-customer",
            "the console refunding a customer's payment is the customer's history, not the console's");
        recorded.ActorId.Should().Be("actor-1");
        recorded.UserId.Should().Be("user-1");
        recorded.Amount.Should().Be(12.5m);
        recorded.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task An_unidentified_caller_records_nothing()
    {
        _context.Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(null, null));

        await Trail().RecordAsync(
            new PaymentAuditEntry("PaymentCreate", "Payment", null, "Rejected"), "corr-1", CancellationToken.None);

        _written.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_audit_write_never_fails_the_payment()
    {
        Caller("org-1");
        _repository
            .Setup(repository => repository.AppendAsync(It.IsAny<PaymentAuditEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var record = () => Trail().RecordAsync(
            new PaymentAuditEntry("PaymentCapture", "Payment", "pay-1", "Succeeded"), "corr-1", CancellationToken.None);

        await record.Should().NotThrowAsync(
            "a capture that happened must not be reported as one that did not, and invite a retry");
    }

    [Fact]
    public async Task A_rate_limited_call_is_not_recorded()
    {
        Caller("org-1");

        await Trail().RecordAsync(
            new PaymentAuditEntry("PaymentCreate", "Payment", null, "Rejected", "payment_rate_limited",
                FailureKind: PaymentFailureKind.RateLimited),
            "corr-1",
            CancellationToken.None);

        _written.Should().BeEmpty("nothing was attempted, and the collection is kept forever");
    }

    [Fact]
    public async Task A_caller_that_disconnects_after_a_refund_still_leaves_its_record()
    {
        Caller("org-1");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Trail().RecordAsync(
            new PaymentAuditEntry("PaymentRefund", "Payment", "pay-1", "Succeeded"), "corr-1", cancelled.Token);

        _repository.Verify(repository => repository.AppendAsync(
            It.IsAny<PaymentAuditEvent>(), CancellationToken.None), Times.Once,
            "the refund already happened; the request going away must not cancel its record");
    }

    [Theory]
    [InlineData("org-1", "org-1")]
    [InlineData("console-org", null)]
    [InlineData(null, null)]
    public async Task An_organization_reads_only_its_own_events_and_the_console_reads_the_tenant(
        string? callerOrganization,
        string? expectedFilter)
    {
        Caller(callerOrganization);
        _repository
            .Setup(repository => repository.ListAsync(
                TenantId, expectedFilter, "Payment", "pay-1", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var events = await Trail().ListAsync("Payment", "pay-1", 0, "corr-1", CancellationToken.None);

        events.Should().NotBeNull();
        _repository.Verify(repository => repository.ListAsync(
            TenantId, expectedFilter, "Payment", "pay-1", 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void Caller(string? organizationId) =>
        _context.Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(TenantId, "actor-1", organizationId, "user-1"),
                null));

    private PaymentAuditTrail Trail() => new(
        _repository.Object,
        _context.Object,
        Options.Create(new PaymentOptions { ConsoleOrganizationId = "console-org" }),
        NullLogger<PaymentAuditTrail>.Instance);
}
