using Blocks.Genesis;

namespace Utility.DomainService.Shared.Utilities
{
    /// <summary>
    /// Decides which tenant a request may act on. The answer is always the caller's own tenant, as
    /// Genesis resolved it from the token and <c>x-blocks-key</c>.
    /// </summary>
    /// <remarks>
    /// Request bodies still carry a <c>ProjectKey</c> from the obsolete <c>IProjectKey</c> contract,
    /// and several flows used to honour it as-is. That let any signed-in user of one tenant point a
    /// magic link, or a template-engine query, at another tenant's database. Acting for another
    /// project goes through Genesis instead: the caller presents a token for that project, so the
    /// context tenant already is the target. A <c>ProjectKey</c> is therefore only accepted when it
    /// names the caller's own tenant, so existing callers that send their own key keep working.
    /// </remarks>
    public static class CallerTenant
    {
        public const string MismatchMessage = "ProjectKey must match the tenant of the caller.";

        /// <summary>
        /// The tenant to act on, or null when there is no authenticated tenant or
        /// <paramref name="requestedProjectKey"/> names a different tenant.
        /// </summary>
        public static string? Resolve(string? requestedProjectKey)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(requestedProjectKey) ||
                   string.Equals(requestedProjectKey.Trim(), tenantId, StringComparison.OrdinalIgnoreCase)
                ? tenantId
                : null;
        }
    }
}
