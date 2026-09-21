using Blocks.Genesis;

namespace Utility.DomainService.Storage
{
    /// <summary>
    /// Runs storage calls as a fixed service principal, within the current tenant.
    /// </summary>
    /// <remarks>
    /// The storage driver authorizes by who is calling. A file created with
    /// <c>ObjectAccessLevel = Creator</c> is readable only by the user id that created it, so files a
    /// service owns on everyone's behalf -- financial documents -- are created and read under one
    /// stable principal: nobody else can list or download them through the storage API, while this
    /// service still can, having done its own authorization first.
    /// <para>
    /// Without it the id would be whoever happened to be in context: the user-less worker writing,
    /// then a subscriber reading, and a Creator-locked file would refuse the second.
    /// </para>
    /// <para>
    /// Only the identity changes. Tenant, organization and domain are carried over, so the call
    /// still lands in the caller's own tenant database and storage container.
    /// </para>
    /// </remarks>
    public static class StorageServiceIdentity
    {
        public static IDisposable Enter(string principalId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(principalId);

            var previous = BlocksContext.GetContext();

            if (string.IsNullOrEmpty(previous?.TenantId))
            {
                // Without a tenant the call cannot be placed anywhere safe; refusing is better than
                // a service principal acting in no tenant at all.
                throw new InvalidOperationException(
                    "A storage service identity needs a tenant in context.");
            }

            BlocksContext.SetContext(BlocksContext.Create(
                previous.TenantId,
                Array.Empty<string>(),
                principalId,
                true,
                previous.RequestUri,
                previous.OrganizationId,
                previous.ExpireOn,
                string.Empty,
                Array.Empty<string>(),
                principalId,
                string.Empty,
                principalId,
                string.Empty,
                previous.OriginalTenantId,
                previous.ApplicationDomain));

            return new Scope(previous);
        }

        private sealed class Scope(BlocksContext previous) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                BlocksContext.SetContext(previous);
            }
        }
    }
}
