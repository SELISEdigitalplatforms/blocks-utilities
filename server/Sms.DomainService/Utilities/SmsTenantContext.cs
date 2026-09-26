using Blocks.Genesis;

namespace Sms.DomainService.Utilities;

/// <summary>
/// Puts a tenant on the ambient <see cref="BlocksContext"/> for work that has no caller: the
/// worker's queue consumers and the provider webhooks.
/// </summary>
/// <remarks>
/// Flagged authenticated, unlike Payment's equivalent, because Blocks Secrets refuses an
/// unauthenticated context even for a <c>service</c> secret, and reading the tenant's provider key
/// is the reason this exists. The context carries no roles or permissions and only the tenant id
/// that the queue item or the (signature-checked) webhook route named. Restores whatever context
/// was there before on dispose.
/// </remarks>
public static class SmsTenantContext
{
    public const string SystemUserId = "sms-system";

    public static IDisposable Enter(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("A tenant identifier is required.", nameof(tenantId));
        }

        var previous = BlocksContext.GetContext();
        var normalized = tenantId.Trim();
        BlocksContext.SetContext(BlocksContext.Create(
            normalized,
            [],
            SystemUserId,
            true,
            string.Empty,
            string.Empty,
            DateTime.MinValue,
            string.Empty,
            [],
            SystemUserId,
            string.Empty,
            string.Empty,
            string.Empty,
            normalized,
            string.Empty));

        return new Restore(previous);
    }

    private sealed class Restore(BlocksContext? previous) : IDisposable
    {
        public void Dispose()
        {
            if (previous == null)
            {
                BlocksContext.ClearContext();
            }
            else
            {
                BlocksContext.SetContext(previous);
            }
        }
    }
}
