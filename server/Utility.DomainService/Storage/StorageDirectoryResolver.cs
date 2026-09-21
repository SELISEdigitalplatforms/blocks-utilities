using System.Collections.Concurrent;
using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Storage.DomainService.Enums;
using Storage.DomainService.Services;

namespace Utility.DomainService.Storage
{
    /// <summary>
    /// Turns a logical directory name into the id of a real storage directory, creating it on first use.
    /// </summary>
    /// <remarks>
    /// Storage driver 4.1.2 authorizes every upload against its parent directory and refuses one whose
    /// parent does not exist. The ids this service used to pass ("Blocks-PDF-Generated-Files" and the
    /// like) were never real directories -- 4.0.0 simply never checked -- so every upload failed once
    /// the driver was upgraded.
    /// <para>
    /// The driver generates directory ids itself, so a directory is found again by its module name,
    /// which is the configured name: <c>StorageDirectories:&lt;logical name&gt;</c>, falling back to the
    /// logical name. That is what lets each environment keep its own (e.g. "..._dev").
    /// </para>
    /// <para>
    /// Created at the root with no access level, so the background worker -- which acts without a
    /// user -- passes the driver's default-access rule when it uploads into it.
    /// </para>
    /// </remarks>
    public class StorageDirectoryResolver
    {
        public const string ConfigurationSection = "StorageDirectories";

        private readonly IFileDirectoryRepository _repository;
        private readonly IFileDirectoryManagementService _directories;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StorageDirectoryResolver> _logger;

        // Per tenant: each tenant has its own storage database, so the same name is a different id in each.
        private readonly ConcurrentDictionary<(string TenantId, string Name), string> _ids = new();

        public StorageDirectoryResolver(
            IFileDirectoryRepository repository,
            IFileDirectoryManagementService directories,
            IConfiguration configuration,
            ILogger<StorageDirectoryResolver> logger)
        {
            _repository = repository;
            _directories = directories;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>The configured directory name for a logical one.</summary>
        public string NameFor(string logicalName) =>
            _configuration[$"{ConfigurationSection}:{logicalName}"] is { Length: > 0 } configured
                ? configured
                : logicalName;

        /// <summary>
        /// The real directory id for <paramref name="logicalName"/>, or null when it could neither be
        /// found nor created.
        /// </summary>
        /// <param name="objectAccessLevel">
        /// The driver's object access level to create the directory with -- e.g. <c>"Creator"</c> for
        /// one only its creating principal may use. Applied on creation only: an existing directory is
        /// never rewritten, only reported when it does not match.
        /// </param>
        public virtual async Task<string?> ResolveAsync(
            string logicalName,
            string? objectAccessLevel = null,
            CancellationToken cancellationToken = default)
        {
            var name = NameFor(logicalName);

            // Never cached under a shared key. A directory id means nothing outside the tenant whose
            // database holds it, so caching one against an unknown tenant would hand tenant A's
            // directory to tenant B's upload the moment the ambient context is missing.
            var tenantId = BlocksContext.GetContext()?.TenantId;
            var key = string.IsNullOrEmpty(tenantId) ? null : (string?)tenantId;

            if (key is not null && _ids.TryGetValue((key, name), out var cached))
            {
                return cached;
            }

            if (key is null)
            {
                _logger.LogWarning(
                    "StorageDirectoryResolver: resolving {LogicalName} with no tenant in context; not cached",
                    logicalName);
            }

            var existing = await _repository.GetDefaultDirectoryByModuleNameAsync(name, cancellationToken);
            var id = existing?.ItemId;

            if (existing is not null &&
                !string.Equals(existing.ObjectAccessLevel?.ToString(), objectAccessLevel, StringComparison.OrdinalIgnoreCase) &&
                !(existing.ObjectAccessLevel is null && string.IsNullOrEmpty(objectAccessLevel)))
            {
                // Not corrected here: changing who may use a directory is an access decision, not
                // something to do silently on the upload path. Loud, so it is noticed.
                _logger.LogWarning(
                    "StorageDirectoryResolver: directory Name={Name} Id={Id} has access level {Actual}, expected {Expected}",
                    name, id, existing.ObjectAccessLevel?.ToString() ?? "none", objectAccessLevel ?? "none");
            }

            if (id is null)
            {
                var created = await _directories.CreateDirectoryAsync(
                    name,
                    parentDirectoryId: null,
                    description: name,
                    moduleName: name,
                    objectAccessLevel: objectAccessLevel,
                    cancellationToken: cancellationToken);

                // NameConflict: another worker created it between the lookup and the insert. Theirs stands.
                id = created.IsSuccess
                    ? created.DirectoryId
                    : created.Status == DirectoryOperationStatus.NameConflict
                        ? (await _repository.GetDefaultDirectoryByModuleNameAsync(name, cancellationToken))?.ItemId
                        : null;

                if (id is null)
                {
                    _logger.LogError(
                        "StorageDirectoryResolver: could not create directory Name={Name} Status={Status}",
                        name, created.Status);
                    return null;
                }

                _logger.LogInformation("StorageDirectoryResolver: created directory Name={Name} Id={Id}", name, id);
            }

            if (key is not null)
            {
                _ids[(key, name)] = id;
            }

            _logger.LogInformation(
                "StorageDirectoryResolver: resolved {LogicalName} to directory Name={Name} Id={Id}",
                logicalName, name, id);

            return id;
        }
    }
}
