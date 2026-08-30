using Subscription.DomainService.Entities;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// Stores and reads the mail delivery history.
/// </summary>
public interface IMailDeliveryReportRepository
{
    /// <summary>Appends one report. Never updates an existing row.</summary>
    Task AddAsync(MailDeliveryReport report, CancellationToken cancellationToken);

    /// <summary>Every mail sent about one subject, newest first.</summary>
    Task<IReadOnlyList<MailDeliveryReport>> ForSubjectAsync(
        string tenantId,
        string subjectId,
        CancellationToken cancellationToken);

    /// <summary>The most recent mail for a tenant, newest first.</summary>
    Task<IReadOnlyList<MailDeliveryReport>> RecentAsync(
        string tenantId,
        int limit,
        CancellationToken cancellationToken);
}
