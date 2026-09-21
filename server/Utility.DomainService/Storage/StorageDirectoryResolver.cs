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
        public virtual async Task<string?> ResolveAsync(string logicalName, CancellationToken cancellationToken = default)
        {
            var name = NameFor(logicalName);
            var key = (BlocksContext.GetContext()?.TenantId ?? string.Empty, name);

            if (_ids.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var id = (await _repository.GetDefaultDirectoryByModuleNameAsync(name, cancellationToken))?.ItemId;

            if (id is null)
            {
                var created = await _directories.CreateDirectoryAsync(
                    name,
                    parentDirectoryId: null,
                    description: name,
                    moduleName: name,
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

            _ids[key] = id;
            return id;
        }
    }
}
