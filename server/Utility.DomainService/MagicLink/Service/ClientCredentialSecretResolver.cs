using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Utility.DomainService.MagicLink.Models;

namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Finds the client secret a magic link sends to the IdP token endpoint.
    /// </summary>
    public interface IClientCredentialSecretResolver
    {
        /// <summary>
        /// The secret of <paramref name="credential"/>, or null when it cannot be read.
        /// </summary>
        /// <param name="credential">A credential read from the project's <c>ClientCredentials</c>.</param>
        /// <param name="projectKey">The project the credential belongs to; its secret store is read.</param>
        Task<string?> GetSecretAsync(ClientCredential credential, string projectKey, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reads a client-credential secret from Blocks Secrets, falling back to the plaintext field for
    /// credentials saved before blocks-iam moved them there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference rule, as in blocks-iam: a non-empty <see cref="ClientCredential.ClientSecretReference"/>
    /// is authoritative and its value lives in the project's secret store; an empty one means
    /// <see cref="ClientCredential.ClientSecret"/> holds the plaintext. When a reference is set the
    /// plaintext field is never used, not even when the read fails.
    /// </para>
    /// <para>
    /// The read runs in the worker, with no caller. Blocks Secrets refuses an unauthenticated
    /// context even for a <c>service</c> secret and picks the store from the context's tenant, so
    /// the read runs as <see cref="SystemUserId"/> in the link's project and the previous context is
    /// restored afterwards. <c>ISecretService</c> is scoped, so a scope is created per read. Nothing
    /// is cached: a rotation in the console is picked up by the next link.
    /// </para>
    /// </remarks>
    public sealed class ClientCredentialSecretResolver : IClientCredentialSecretResolver
    {
        public const string SystemUserId = "utilities-magic-link";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ClientCredentialSecretResolver> _logger;

        public ClientCredentialSecretResolver(IServiceScopeFactory scopeFactory, ILogger<ClientCredentialSecretResolver> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<string?> GetSecretAsync(ClientCredential credential, string projectKey, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(credential);

            var reference = credential.ClientSecretReference;
            if (string.IsNullOrEmpty(reference))
            {
                return credential.ClientSecret;
            }

            if (string.IsNullOrWhiteSpace(projectKey))
            {
                _logger.LogWarning("The secret of client credential {ClientId} cannot be read without a project.", credential.ItemId);
                return null;
            }

            var previous = BlocksContext.GetContext();
            try
            {
                BlocksContext.SetContext(ServiceContext(projectKey.Trim()));

                using var scope = _scopeFactory.CreateScope();
                var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
                var value = await secrets.GetValueAsync(reference, cancellationToken);

                if (string.IsNullOrEmpty(value))
                {
                    _logger.LogWarning("The secret of client credential {ClientId} is empty in Blocks Secrets.", credential.ItemId);
                    return null;
                }

                return value;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Not found, locked, deleted, access denied or the vault unreachable. Secret* messages
                // carry the reference id, so only the type is logged.
                _logger.LogWarning("The secret of client credential {ClientId} is unavailable: {FailureClass}.", credential.ItemId, ex.GetType().Name);
                return null;
            }
            finally
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

        private static BlocksContext ServiceContext(string tenantId) =>
            BlocksContext.Create(
                tenantId,
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
                SystemUserId,
                string.Empty,
                tenantId,
                string.Empty);
    }
}
