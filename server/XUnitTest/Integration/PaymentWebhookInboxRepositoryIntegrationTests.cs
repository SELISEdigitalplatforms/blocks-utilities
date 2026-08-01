using FluentAssertions;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;

namespace XUnitTest.Integration;

[Collection(MongoIntegrationCollection.Name)]
public sealed class PaymentWebhookInboxRepositoryIntegrationTests
{
    private readonly PaymentWebhookInboxRepository _repository;

    public PaymentWebhookInboxRepositoryIntegrationTests(MongoIntegrationFixture fixture) =>
        _repository = new PaymentWebhookInboxRepository(fixture.DbContextProvider);

    private static PaymentWebhookInbox NewWebhook(string tenantId) => new()
    {
        WebhookId = Guid.NewGuid().ToString("N"),
        TenantId = tenantId,
        WebhookType = "standard",
        EventCode = "AUTHORISATION",
        DeduplicationKey = Guid.NewGuid().ToString(),
        NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1),
        CreatedAtUtc = DateTime.UtcNow,
        Status = PaymentWebhookStatus.Pending
    };

    [Fact]
    public async Task Store_persists_and_duplicate_dedup_key_is_rejected()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var webhook = NewWebhook(tenantId);

        (await _repository.StoreAsync(webhook, CancellationToken.None))
            .Should().Be(WebhookStoreResult.Stored);

        var duplicate = NewWebhook(tenantId);
        duplicate.DeduplicationKey = webhook.DeduplicationKey;
        (await _repository.StoreAsync(duplicate, CancellationToken.None))
            .Should().Be(WebhookStoreResult.Duplicate);
    }

    [Fact]
    public async Task Claim_then_mark_processed_lifecycle()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var webhook = NewWebhook(tenantId);
        await _repository.StoreAsync(webhook, CancellationToken.None);

        var due = await _repository.GetDueAsync(tenantId, DateTime.UtcNow, 50, CancellationToken.None);
        due.Should().Contain(w => w.WebhookId == webhook.WebhookId);

        var leaseId = Guid.NewGuid().ToString();
        var claimed = await _repository.TryClaimAsync(
            tenantId, webhook.WebhookId, leaseId, DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        claimed.Should().NotBeNull();
        claimed!.Status.Should().Be(PaymentWebhookStatus.Processing);

        await _repository.MarkProcessedAsync(tenantId, webhook.WebhookId, leaseId, CancellationToken.None);
        var afterProcessed = await _repository.GetDueAsync(tenantId, DateTime.UtcNow, 50, CancellationToken.None);
        afterProcessed.Should().NotContain(w => w.WebhookId == webhook.WebhookId);
    }

    [Fact]
    public async Task MarkFailed_reschedules_for_retry()
    {
        var tenantId = MongoIntegrationFixture.NewTenantId();
        var webhook = NewWebhook(tenantId);
        await _repository.StoreAsync(webhook, CancellationToken.None);
        var leaseId = Guid.NewGuid().ToString();
        await _repository.TryClaimAsync(
            tenantId, webhook.WebhookId, leaseId, DateTime.UtcNow.AddMinutes(5), CancellationToken.None);

        await _repository.MarkFailedAsync(
            tenantId, webhook.WebhookId, leaseId, PaymentWebhookStatus.RetryScheduled, 3,
            DateTime.UtcNow.AddMinutes(-1), CancellationToken.None);

        var due = await _repository.GetDueAsync(tenantId, DateTime.UtcNow, 50, CancellationToken.None);
        var stored = due.Single(w => w.WebhookId == webhook.WebhookId);
        stored.Status.Should().Be(PaymentWebhookStatus.RetryScheduled);
        stored.AttemptCount.Should().Be(3);
    }
}
