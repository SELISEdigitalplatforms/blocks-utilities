using MongoDB.Driver;
using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// The indexes behind the three questions this collection is opened to answer.
/// </summary>
public static class MailDeliveryReportIndexDefinitions
{
    public const string SubjectIndexName = "mail_report_by_subject";
    public const string RecentIndexName = "mail_report_by_tenant_created";
    public const string MessageIndexName = "mail_report_by_message_id";
    public const string PurgeIndexName = "ttl_mail_report_purge_at";

    public static IReadOnlyCollection<CreateIndexModel<MailDeliveryReport>> CreateIndexes() =>
    [
        // "Every mail we ever sent about this invoice", which is the question an operator arrives
        // with. Newest first, because a resend is what they are usually looking for.
        new(
            Builders<MailDeliveryReport>.IndexKeys
                .Ascending(report => report.TenantId)
                .Ascending(report => report.SubjectId)
                .Descending(report => report.CreatedAtUtc),
            new CreateIndexOptions { Name = SubjectIndexName }),

        // "What has gone out lately", for a tenant, without scanning the collection.
        new(
            Builders<MailDeliveryReport>.IndexKeys
                .Ascending(report => report.TenantId)
                .Descending(report => report.CreatedAtUtc),
            new CreateIndexOptions { Name = RecentIndexName }),

        // Not unique, deliberately. A document's message id is stable across a deliberate resend,
        // so two rows sharing one is the honest record of it having been sent twice on purpose.
        // Made unique, the second send would fail to record and the history would show one mail
        // where two went out.
        new(
            Builders<MailDeliveryReport>.IndexKeys
                .Ascending(report => report.TenantId)
                .Ascending(report => report.MailMessageId),
            new CreateIndexOptions
            {
                Name = MessageIndexName,
                Sparse = true
            }),

        // Retention. Every row is final when written, so unlike the work queue's TTL there is no
        // pending state to keep alive by leaving this null -- PurgeAtUtc is always set, and
        // ExpireAfter zero means "remove when that moment passes".
        new(
            Builders<MailDeliveryReport>.IndexKeys.Ascending(report => report.PurgeAtUtc),
            new CreateIndexOptions
            {
                Name = PurgeIndexName,
                ExpireAfter = TimeSpan.Zero
            })
    ];
}
