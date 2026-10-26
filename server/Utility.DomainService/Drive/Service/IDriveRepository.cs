using Utility.DomainService.Drive.Models;

namespace Utility.DomainService.Drive.Service;

public interface IDriveRepository
{
    Task<DriveConnection?> GetConnectionAsync(string projectKey, string userId, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces the one connection for this project and user.</summary>
    Task UpsertConnectionAsync(DriveConnection connection, CancellationToken cancellationToken);

    Task<bool> DeleteConnectionAsync(string projectKey, string userId, CancellationToken cancellationToken);

    Task TouchConnectionAsync(string projectKey, string userId, DateTime lastUsedAt, CancellationToken cancellationToken);

    Task InsertStateAsync(DriveOAuthState state, CancellationToken cancellationToken);

    /// <summary>Returns the state and deletes it in the same call, so a replayed callback finds nothing.</summary>
    Task<DriveOAuthState?> TakeStateAsync(string stateId, CancellationToken cancellationToken);
}
