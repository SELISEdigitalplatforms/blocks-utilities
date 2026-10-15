using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>How a credit grant came to exist.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CreditGrantSource
{
    /// <summary>Paid for by the subscriber, with a purchase record and an invoice behind it.</summary>
    Purchased = 0,

    /// <summary>Given without a charge, with a reason, by someone allowed to manage the subscription.</summary>
    AdminGranted = 1,

    /// <summary>Split off another grant on the same subscription and meter, keeping its expiry.</summary>
    Moved = 2
}
