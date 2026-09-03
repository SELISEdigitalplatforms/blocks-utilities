using System.Diagnostics;
using Payment.DomainService.Utilities;

namespace Subscription.DomainService.Scheduling;

/// <summary>
/// The spans the background work queue produces: one per attempt, and one per repair sweep pass.
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

    /// <summary>
    /// One repair sweep pass over one tenant.
    /// </summary>
    /// <remarks>
    /// The sweep announces work rather than running it, so its lines were the ones still arriving
    /// with an empty trace id after attempts had theirs: an attempt runs inside a span, and the
    /// scheduling that produced it did not.
    /// <para>
    /// Giving the pass a span closes that, and does a second thing worth more: it is what
    /// <see cref="CurrentTraceParent"/> reads when the sweep queues an item, so an attempt that
    /// starts minutes later links back to the pass that found it. Until this existed the sweep
    /// stored no context and the link was dead weight for every item it announced — which is most
    /// of them.
    /// </para>
    /// <para>
    /// Internal, not Consumer: nothing handed this work to the sweep. It went looking.
    /// </para>
    /// </remarks>
    public static Activity? StartRepairSweep(string tenantId)
    {
        var activity = Source.StartActivity("subscription.repair_sweep", ActivityKind.Internal);

        activity?.SetTag("subscription.tenant_id", PaymentLogValue.Id(tenantId));

        return activity;
    }
}
