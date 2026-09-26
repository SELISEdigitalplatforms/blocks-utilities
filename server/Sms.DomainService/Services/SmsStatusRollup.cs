using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Services;

/// <summary>The message status is a summary of its recipients; this is the one place that summarizes.</summary>
public static class SmsStatusRollup
{
    /// <summary>After a send round with nothing left pending.</summary>
    public static SmsMessageStatus AfterSend(IReadOnlyCollection<SmsRecipient> recipients) =>
        recipients.Any(r => r.Status != SmsRecipientStatus.Failed)
            ? SmsMessageStatus.Submitted
            : SmsMessageStatus.Failed;

    /// <summary>Null until every recipient has a final outcome.</summary>
    public static SmsMessageStatus? AfterDelivery(IReadOnlyCollection<SmsRecipient> recipients)
    {
        if (recipients.Count == 0 ||
            recipients.Any(r => r.Status is SmsRecipientStatus.Pending or SmsRecipientStatus.Submitted))
        {
            return null;
        }

        var delivered = recipients.Count(r => r.Status == SmsRecipientStatus.Delivered);
        if (delivered == recipients.Count)
        {
            return SmsMessageStatus.Delivered;
        }

        if (delivered > 0)
        {
            return SmsMessageStatus.PartiallyDelivered;
        }

        if (recipients.All(r => r.Status == SmsRecipientStatus.Failed))
        {
            return SmsMessageStatus.Failed;
        }

        return recipients.All(r => r.Status is SmsRecipientStatus.Undelivered or SmsRecipientStatus.Failed)
            ? SmsMessageStatus.Undelivered
            : SmsMessageStatus.DeliveryFailed;
    }
}
