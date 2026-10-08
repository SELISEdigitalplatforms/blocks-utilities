using System.Collections.Concurrent;

namespace Worker;

/// <summary>
/// The roster tenants a sweep found with no database, and when each is worth trying again.
/// </summary>
/// <remarks>
/// The roster is discovered rather than curated, so on prod about a thousand of its tenants have
/// no database here. Trying each one every pass cost two lines apiece -- Genesis logging
/// "Failed to initialize database" as an error, then the sweep's own skip warning -- which came to
/// roughly 95 lines a second (2026-10-08) and buried every failure worth reading. Remembering them
/// stops both, since the database is never asked for while a tenant is held here.
/// <para>
/// Held for <see cref="RecheckAfter"/>, not forever: a tenant provisioned later must still be
/// reached, and the recheck is the only thing that notices. A success forgets the tenant at once.
/// </para>
/// </remarks>
internal sealed class UnprovisionedTenants(TimeProvider time)
{
    // ponytail: in memory, per sweep. A restart relearns the set in one pass at one line per
    // tenant; share it across replicas only if that first pass ever matters.
    internal static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _recheckAt = new(StringComparer.Ordinal);

    public bool ShouldSkip(string tenantId) =>
        _recheckAt.TryGetValue(tenantId, out var recheckAt) && time.GetUtcNow() < recheckAt;

    /// <returns>True the first time a tenant is remembered -- the one time it is worth a log line.</returns>
    public bool Remember(string tenantId)
    {
        var first = !_recheckAt.ContainsKey(tenantId);
        _recheckAt[tenantId] = time.GetUtcNow() + RecheckAfter;

        return first;
    }

    public void Forget(string tenantId) => _recheckAt.TryRemove(tenantId, out _);
}
