using MongoDB.Bson.Serialization.Attributes;

namespace Utility.DomainService.Drive.Models;

/// <summary>
/// One user's Microsoft account connection inside one project. Holds the refresh token only;
/// access tokens are minted on demand and kept in memory.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class DriveConnection
{
    public const string CollectionName = "DriveConnections";

    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string ProjectKey { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    /// <summary>AES-GCM payload: nonce, tag, ciphertext, base64.</summary>
    public string RefreshTokenCiphertext { get; set; } = string.Empty;

    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>The <c>tid</c> claim. Diagnostics only: which Microsoft directory this account is in.</summary>
    public string? MicrosoftTenantId { get; set; }

    public string? AccountDisplayName { get; set; }

    public string? AccountEmail { get; set; }

    public string? Scopes { get; set; }

    public DateTime ConnectedAt { get; set; }

    public DateTime LastUsedAt { get; set; }
}

/// <summary>
/// The OAuth <c>state</c> handed to Entra, with everything the callback needs to finish. Single
/// use and short-lived; a TTL index removes the ones that never come back.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class DriveOAuthState
{
    public const string CollectionName = "DriveOAuthStates";

    [BsonId]
    public string ItemId { get; set; } = string.Empty;

    public string ProjectKey { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = string.Empty;

    public string CodeVerifier { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}
