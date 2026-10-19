using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>How far a credit purchase has got.</summary>
/// <remarks>
/// Written before the card is charged and advanced after each step, so a crash at any point leaves
/// a record the recovery sweep can resume from. <see cref="Pending"/>, <see cref="AwaitingPayment"/>
/// and <see cref="Charged"/> are the states it acts on.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CreditPurchaseStatus
{
    /// <summary>Quoted and recorded. The charge may or may not have reached the provider.</summary>
    Pending = 0,

    /// <summary>The provider took the money. The grant, invoice or events may still be missing.</summary>
    Charged = 1,

    /// <summary>Charged, granted, invoiced and announced.</summary>
    Finalized = 2,

    /// <summary>Not charged: declined, refused, or never reached the provider.</summary>
    Failed = 3,

    /// <summary>Unresolved past the alert deadline. An operator has to look at it.</summary>
    NeedsAttention = 4,

    /// <summary>
    /// A hosted checkout is open for the buyer to pay at, and the provider has not decided it yet.
    /// Only a purchase on a subscription with no saved card reaches this (spec 003).
    /// </summary>
    AwaitingPayment = 5
}
