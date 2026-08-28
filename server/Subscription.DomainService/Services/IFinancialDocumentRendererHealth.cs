namespace Subscription.DomainService.Services;

/// <summary>
/// What the last renderer probe found, shared by whoever ran it and whoever needs to know before
/// touching document delivery.
/// </summary>
/// <remarks>
/// A circuit breaker with exactly two states and no half-open dance: the only thing either side
/// of it can do is probe with a real render, and a render either produces bytes or it does not.
/// There is no cheaper signal to sample in between that would tell the difference between "still
/// broken" and "about to work again", so nothing here tries to guess — it reports the last probe's
/// answer and lets the periodic monitor keep asking.
/// <para>
/// Deliberately narrower than a general health-check abstraction. This exists for one dependency
/// that one part of the worker must stop calling when it is down, not as a place to grow a health
/// system — see <see cref="FinancialDocumentRendererHealthGate"/>'s remarks for why a generic one
/// was rejected.
/// </para>
/// </remarks>
public interface IFinancialDocumentRendererHealth
{
    /// <summary>
    /// True until a probe fails, false until a probe afterwards succeeds. Starts true: a worker
    /// that has not probed yet has no evidence the renderer is down, and treating "unknown" as
    /// "unhealthy" would refuse document delivery for however long startup takes even when
    /// Chromium is perfectly fine.
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>Records a probe that produced PDF bytes.</summary>
    void RecordSuccess();

    /// <summary>Records a probe that threw, timed out, or produced nothing.</summary>
    void RecordFailure(Exception? exception, string reason);
}
