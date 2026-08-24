namespace Subscription.DomainService.Utilities;

/// <summary>
/// Gates the subscription simulation test harness — a diagnostic surface that can advance a
/// subscription's lifecycle without waiting for calendar time.
/// </summary>
/// <remarks>
/// Defaults closed. This is checked twice: once at service registration, where a
/// <see cref="Enabled"/> of <c>true</c> in a Production <c>IHostEnvironment</c> throws rather
/// than starts, and again on every request, since remote configuration (this repository loads
/// config from a Mongo-backed secrets collection) can flip the value after the process has
/// already started without a restart to catch it at.
/// </remarks>
public sealed class SubscriptionSimulationOptions
{
    public const string SectionName = "SubscriptionSimulation";

    public bool Enabled { get; set; }

    /// <summary>
    /// A second, independent gate for the allowlisted data console — the one piece of this
    /// harness that reads and writes Mongo documents directly rather than going through a
    /// domain service. Requires <see cref="Enabled"/> as well; this can narrow access further
    /// within an environment where the harness itself is on, but never widen it.
    /// </summary>
    public bool DataConsoleEnabled { get; set; }
}
