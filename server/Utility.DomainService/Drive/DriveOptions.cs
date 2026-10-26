namespace Utility.DomainService.Drive;

/// <summary>
/// Settings for the OneDrive / SharePoint integration (spec 004).
/// </summary>
/// <remarks>
/// Bound from the <c>Drive</c> section. The non-secret values live in <c>appsettings*.json</c>;
/// <see cref="ClientId"/>, <see cref="ClientSecret"/>, <see cref="RedirectUrl"/> and
/// <see cref="TokenEncryptionKey"/> come from the Blocks Secrets document that
/// <c>AddMongoDbConfiguration</c> merges into the same section at startup, so one options object
/// sees both without the code having to know which store a value came from.
/// </remarks>
public sealed class DriveOptions
{
    public const string SectionName = "Drive";

    /// <summary>Entra app (client) id of the platform-owned multi-tenant registration.</summary>
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>Absolute URL of the callback endpoint as registered on the Entra app.</summary>
    public string? RedirectUrl { get; set; }

    /// <summary>Base64 of 32 random bytes; the AES-GCM key refresh tokens are stored under.</summary>
    public string? TokenEncryptionKey { get; set; }

    /// <summary>
    /// Identifier stored beside every ciphertext so a second key can be introduced later without
    /// guessing which key an old row was written with.
    /// </summary>
    public string TokenEncryptionKeyId { get; set; } = "drive-v1";

    /// <summary>
    /// <c>common</c> lets both work and personal accounts in, which is what a multi-tenant app
    /// registered for "any organizational directory and personal accounts" expects.
    /// </summary>
    public string AuthorityBaseUrl { get; set; } = "https://login.microsoftonline.com/common";

    /// <summary>
    /// Admin consent cannot be asked of <c>common</c>: the admin consents for one directory, so the
    /// endpoint is addressed to <c>organizations</c> and Entra picks the admin's own.
    /// </summary>
    public string AdminConsentBaseUrl { get; set; } = "https://login.microsoftonline.com/organizations";

    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    public string Scopes { get; set; } =
        "Files.ReadWrite.All Sites.ReadWrite.All offline_access User.Read openid profile email";

    public long MaxFileSizeBytes { get; set; } = 250L * 1024 * 1024;

    /// <summary>
    /// Graph requires upload-session chunks to be a multiple of 320 KiB; 10 MiB is 32 of them.
    /// </summary>
    public int UploadChunkSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Graph's simple-upload ceiling. Anything larger needs an upload session.</summary>
    public long SimpleUploadLimitBytes { get; set; } = 4L * 1024 * 1024;

    public int MaxUploadFilesPerRequest { get; set; } = 20;

    public int StateTtlMinutes { get; set; } = 10;

    public int MaxRetries { get; set; } = 3;

    /// <summary>Used when a throttled response carries no <c>Retry-After</c>.</summary>
    public int DefaultRetryAfterSeconds { get; set; } = 2;

    public int DefaultPageSize { get; set; } = 50;

    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Lets <c>http://localhost</c> return URLs through the domain check. Development only.
    /// </summary>
    public bool AllowLocalhostReturnUrl { get; set; }

    /// <summary>Logical storage directory imported files are filed under.</summary>
    public string ImportDirectory { get; set; } = "Blocks-Utilities-Drive-Imports";
}
