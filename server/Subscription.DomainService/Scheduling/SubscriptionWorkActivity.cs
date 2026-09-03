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

    /// <summary>
    /// The current trace context as a header value to store on scheduled work, or null when there
    /// is nothing to store.
    /// </summary>
    /// <remarks>
    /// Null outside a request — a sweep, a startup path, a test — which is the ordinary case rather
    /// than a failure. The format check is not defensiveness about a value we produce: an activity
    /// in the legacy hierarchical format has an <c>Id</c> that looks like an identifier and does not
    /// parse as trace context, so storing it would fail silently at the far end a month later.
    /// </remarks>
    public static string? CurrentTraceParent() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } current ? current.Id : null;

    /// <summary>
    /// The scheduling context to link an attempt to, or null when the work carries none.
    /// </summary>
    /// <remarks>
    /// Remote, because it belongs to a different process and usually to a different month. Anything
    /// unparseable is treated as absent: a span that refuses to start because a stored header was
    /// malformed would stop a renewal over a diagnostic.
    /// </remarks>
    public static ActivityContext? SchedulingContext(string? traceParent) =>
        ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out var context)
            ? context
            : null;
}
