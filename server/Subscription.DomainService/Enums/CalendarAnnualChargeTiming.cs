using System.Text.Json.Serialization;

namespace Subscription.DomainService.Enums;

/// <summary>
/// When a calendar-aligned yearly price collects its annual amount.
/// </summary>
/// <remarks>
/// Only meaningful alongside <see cref="BillingAlignment.CalendarMonth"/> on a yearly price, which
/// is the one arrangement with two separate things to charge for: the stub covering the rest of the
/// month, and the year that begins on the first.
/// <para>
/// Serialized by name, like every other enum crossing this boundary — a client that has to know
/// <c>1</c> means prepaid is coupled to our storage format.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CalendarAnnualChargeTiming
{
    /// <summary>
    /// Collect the stub now and the annual amount when the year actually begins.
    /// </summary>
    /// <remarks>
    /// Zero, and therefore what a price authored without an opinion gets. It is also the more
    /// conservative of the two: the subscriber has paid for exactly what they hold, and a year they
    /// have not started is a year they have not paid for.
    /// </remarks>
    AtBoundary = 0,

    /// <summary>
    /// Collect the stub and the whole year together, at checkout.
    /// </summary>
    /// <remarks>
    /// The year is then prepaid: the boundary moves the subscription into it without charging
    /// anything. Nothing is refunded if they cancel during the stub — they bought the year.
    /// </remarks>
    AtCheckout = 1
}
