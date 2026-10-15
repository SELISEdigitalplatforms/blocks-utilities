using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>Where a credit grant is in its life.</summary>
/// <remarks>
/// Only <see cref="Active"/> and <see cref="Exhausted"/> can move again, and only between each
/// other: an exhausted grant becomes active when an enforced recording that drew it to zero is
/// undone. The other two are terminal.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CreditGrantStatus
{
    Active = 0,

    /// <summary>Fully consumed.</summary>
    Exhausted = 1,

    /// <summary>Its usage window ended with credits unused.</summary>
    Expired = 2,

    /// <summary>Lost to a plan change or the end of the subscription. Never refunded.</summary>
    Forfeited = 3
}
