using System.Diagnostics;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// The span every background work attempt runs inside.
/// </summary>
/// <remarks>
/// A worker serves no HTTP request, so the ASP.NET Core instrumentation that gives every API line a
/// trace id never runs here and <see cref="Activity.Current"/> was null for the whole of a work
/// attempt. The log pipeline's trace enricher reads exactly that, which is why the worker's trace id
/// column was empty while the API's was populated — the enricher was working and had nothing to
/// read.
/// <para>
/// The name matches <see cref="SubscriptionWorkMetrics.MeterName"/> deliberately: the same body of
/// work described two ways, found under one name in whichever signal an operator reaches for first.
/// </para>
/// <para>
/// It must be registered with a tracer provider to do anything. Starting an activity from a source
/// nothing listens to returns null and sets no current activity, for the same reason recording to a
/// meter no exporter subscribed to records nothing — see the worker's composition root, which
/// subscribes to both.
/// </para>
/// </remarks>
public static class SubscriptionWorkActivity
{
    /// <summary>The name a tracer provider subscribes to.</summary>
    public const string SourceName = SubscriptionWorkMetrics.MeterName;

    /// <summary>
    /// Shared and never disposed. An <see cref="ActivitySource"/> is a process-wide name rather
    /// than a resource, and disposing one held by a static field would silence every later span in
    /// the process.
    /// </summary>
    public static readonly ActivitySource Source = new(SourceName);
}
