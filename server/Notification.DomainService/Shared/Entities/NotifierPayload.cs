
namespace DomainService.Shared
{
    public class NotifierPayload
    {
        public string ConnectionId { get; set; }

        /// <summary>
        ///     It is only applicable or considered when we want User Specific Notification for multiple users
        /// </summary>
        public List<string> UserIds { get; set; }

        /// <summary>
        /// It is only applicable or considered when we want User Specific Notification for multiple users based on their roles
        /// </summary>
        public List<string> Roles { get; set; }

        /// <summary>
        ///     It is only applicable or considered when we want Filter Specific Notification
        /// </summary>
        public List<SubscriptionFilter>? SubscriptionFilters { get; set; }

        /// <summary>
        ///     It can be BroadcastReceiverType, FilterSpecificReceiverType, UserSpecificReceiverType (from
        ///     NotificationReceiverTypeCodes)
        /// </summary>

       // public NotificationReceiverTypes NotificationType { get; set; }

        /// <summary>
        /// payload(JSON string) you want to store with offline message. This payload will not push through socket
        /// </summary>
        public string DenormalizedPayload { get; set; }

        /// <summary>
        /// This field only considered when notification type is filterSpecific; if it is true then it will store FilterSpecificNotification to DB so that user can grab it later
        /// For UserSpecific and BroadCast all will be automatically stored; in that case this field value is not considered
        /// </summary>
        // public bool EnablePersistence { get; set; }

        public bool SaveDenormalizedPayloadAsAnObject { get; set; }
    }
}
