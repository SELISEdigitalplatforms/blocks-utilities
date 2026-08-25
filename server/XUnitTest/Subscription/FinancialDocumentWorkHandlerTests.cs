using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>
/// What the queue does with a document work item.
/// </summary>
/// <remarks>
/// Two decisions worth pinning. Which id the aggregate is — a payment or a subscription — is read from
/// the work key rather than guessed from the shape of the id, because both are GUIDs and guessing
/// wrong looks up the right id in the wrong collection and finds nothing. And which failures deserve a
/// retry: a render does, a payment that turned out to need no document does not.
/// </remarks>
public sealed class FinancialDocumentWorkHandlerTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<ISubscriptionFinancialDocumentIssuer> _issuer = new();
    private readonly Mock<ISubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionFinancialDocumentDeliveryService> _delivery = new();

    [Fact]
    public async Task Work_naming_a_payment_issues_that_payments_document()
    {
        var outcome = await IssueHandler().ExecuteAsync(
            Work($"{SubscriptionFinancialDocumentAnnouncer.PaymentWorkKeyPrefix}pay-1", "pay-1"),
            CancellationToken.None);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _issuer.Verify(
            issuer => issuer.IssueForPaymentAsync(
                TenantId, "pay-1", "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Work_naming_a_subscription_writes_whatever_that_subscription_owes()
    {
        var outcome = await IssueHandler().ExecuteAsync(
            Work(
                $"{SubscriptionFinancialDocumentAnnouncer.SubscriptionWorkKeyPrefix}sub-1",
                "sub-1"),
            CancellationToken.None);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);

        // Drains the subscription rather than naming one document, so a trial invoice and a credit
        // note recorded moments apart are both written by one visit.
        _issuer.Verify(
            issuer => issuer.IssueForSubscriptionAsync(
                TenantId, "sub-1", "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);

        // And never as a payment, which is the confusion the work-key prefix exists to prevent.
        _issuer.Verify(
            issuer => issuer.IssueForPaymentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Work_naming_a_subscription_that_no_longer_exists_is_not_retried()
    {
        var outcome = await IssueHandler().ExecuteAsync(
            Work(
                $"{SubscriptionFinancialDocumentAnnouncer.SubscriptionWorkKeyPrefix}sub-1",
                "sub-1"),
            CancellationToken.None);

        // A subscription that has gone owes nothing, which the issuer reports as nothing written
        // rather than as a failure. Completed rather than dead-lettered: there is no error here to
        // retry, and nothing for an operator to look at.
        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        outcome.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task Work_naming_nothing_runs_the_recovery_pass()
    {
        var outcome = await IssueHandler().ExecuteAsync(
            Work("sweep:20260825T1200Z", string.Empty),
            CancellationToken.None);

        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
        _issuer.Verify(
            issuer => issuer.IssuePendingAsync(
                TenantId, "corr-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_payment_that_needs_no_document_is_a_decision_rather_than_a_failure()
    {
        _issuer
            .Setup(issuer => issuer.IssueForPaymentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionFinancialDocument?)null);

        var outcome = await IssueHandler().ExecuteAsync(
            Work($"{SubscriptionFinancialDocumentAnnouncer.PaymentWorkKeyPrefix}pay-1", "pay-1"),
            CancellationToken.None);

        // A declined attempt, a foreign order id, a subscription since deleted. Four more attempts
        // would reach the same answer.
        outcome.Result.Should().Be(SubscriptionWorkResult.Completed);
    }

    [Fact]
    public async Task A_delivery_that_did_not_complete_asks_to_be_retried()
    {
        _delivery
            .Setup(delivery => delivery.DeliverAsync(
                TenantId, "doc-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var outcome = await DeliveryHandler().ExecuteAsync(
            Work("document:doc-1", "doc-1"),
            CancellationToken.None);

        // A render or a mail publish that failed is exactly the kind of thing that succeeds next
        // time, and the document counts its own attempts so this cannot retry forever.
        outcome.Result.Should().Be(SubscriptionWorkResult.Retry);
        outcome.ErrorCode.Should().Be("document_delivery_incomplete");
    }

    [Fact]
    public async Task A_delivery_that_completed_is_finished()
    {
        _delivery
            .Setup(delivery => delivery.DeliverAsync(
                TenantId, "doc-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        (await DeliveryHandler().ExecuteAsync(
                Work("document:doc-1", "doc-1"),
                CancellationToken.None))
            .Result.Should().Be(SubscriptionWorkResult.Completed);
    }

    [Fact]
    public async Task Delivery_work_naming_nothing_sweeps_the_tenant()
    {
        await DeliveryHandler().ExecuteAsync(
            Work("sweep:20260825T1200Z", string.Empty),
            CancellationToken.None);

        _delivery.Verify(
            delivery => delivery.DeliverPendingAsync(TenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void The_handlers_claim_the_work_types_they_are_registered_for()
    {
        // Registered by type, so a handler claiming the wrong one would silently never run — and its
        // work would sit in the queue until it was dead-lettered.
        IssueHandler().WorkType.Should().Be(SubscriptionWorkType.FinancialDocumentIssue);
        DeliveryHandler().WorkType.Should().Be(SubscriptionWorkType.FinancialDocumentDelivery);
    }

    private ISubscriptionWorkHandler IssueHandler() =>
        new FinancialDocumentIssueWorkHandler(_issuer.Object);

    private ISubscriptionWorkHandler DeliveryHandler() =>
        new FinancialDocumentDeliveryWorkHandler(_delivery.Object);

    private static SubscriptionBackgroundWork Work(string workKey, string aggregateId) =>
        new()
        {
            TenantId = TenantId,
            WorkKey = workKey,
            AggregateId = aggregateId,
            CorrelationId = "corr-1"
        };
}
