using MongoDB.Bson.Serialization.Attributes;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Entities;

[BsonIgnoreExtraElements]
public class SmsProviderConfiguration
{
    [BsonId]
    public string ItemId { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SmsProviderType ProviderType { get; set; }
    public bool IsDefault { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    /// <summary>E.164 number to send from. Used when no <see cref="SenderName"/> is set.</summary>
    public string SenderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Alphanumeric sender id shown instead of a number (up to 11 characters). Not every country
    /// accepts one; see <see cref="SenderNameExcludedPrefixes"/>.
    /// </summary>
    public string? SenderName { get; set; }

    /// <summary>
    /// Destination prefixes (E.164 country codes) where carriers reject an alphanumeric sender id,
    /// so the number is used instead. Defaults to the North American Numbering Plan; carriers'
    /// rules change, so tenants extend it rather than this code guessing.
    /// </summary>
    public List<string> SenderNameExcludedPrefixes { get; set; } = [.. DefaultSenderNameExcludedPrefixes];

    public static readonly IReadOnlyList<string> DefaultSenderNameExcludedPrefixes = ["+1"];

    /// <summary>Twilio account SID. Not a secret; Telnyx leaves it empty.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>
    /// Blocks Secrets id of the Twilio auth token or Telnyx API key. The value itself never lands
    /// in this document.
    /// </summary>
    public string ApiKeySecretId { get; set; } = string.Empty;
    public string? MessagingProfileId { get; set; }

    /// <summary>Telnyx account public key for webhook signature checks. Public by design.</summary>
    public string? WebhookPublicKey { get; set; }
    public string? StatusCallbackBaseUrl { get; set; }
    public int MaxRetryAttempts { get; set; } = 5;
    public int DeliveryCheckDelayMinutes { get; set; } = 10;
    public SmsRateLimitSettings RateLimit { get; set; } = new();
    public SmsSpamFilterSettings SpamFilter { get; set; } = new();
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// What goes in the provider's From for one recipient: the sender name, unless none is set or the
    /// destination's country rejects names, in which case the number. Null when a name would be
    /// rejected and there is no number to fall back to.
    /// </summary>
    public string? ResolveFrom(string destination)
    {
        var number = string.IsNullOrWhiteSpace(SenderNumber) ? null : SenderNumber;
        if (string.IsNullOrWhiteSpace(SenderName))
        {
            return number;
        }

        var nameRejected = SenderNameExcludedPrefixes.Any(prefix => destination.Trim().StartsWith(prefix, StringComparison.Ordinal));
        return nameRejected ? number : SenderName.Trim();
    }
}

public class SmsRateLimitSettings
{
    /// <summary>SMS (one per recipient) the tenant may send per window.</summary>
    public int TenantMaxPerWindow { get; set; } = 300;
    public int TenantWindowSeconds { get; set; } = 60;
    public int RecipientMaxPerWindow { get; set; } = 5;
    public int RecipientWindowSeconds { get; set; } = 300;
}

public class SmsSpamFilterSettings
{
    public bool Enabled { get; set; } = true;
    public int MaxRecipients { get; set; } = 100;
    public int MaxMessageLength { get; set; } = 1000;
    public SmsUrlPolicy UrlPolicy { get; set; } = SmsUrlPolicy.Flag;

    /// <summary>A message with a URL and any of these terms is blocked, whatever the URL policy.</summary>
    public List<string> BlockedTerms { get; set; } = ["password", "bank", "wallet", "crypto"];
}
