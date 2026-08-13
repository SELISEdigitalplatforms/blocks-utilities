using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Payment.DomainService.Services;

/// <summary>
/// Azure Key Vault adapter, following the construction blocks-os uses in
/// <c>Identifier.DomainService/Certificate/AzureKeyVaultStorage.cs</c>.
/// </summary>
/// <remarks>
/// Deliberately thin: it owns no policy, so the create-only rule lives in
/// <see cref="PaymentKeyRingStore"/> where it can be tested.
///
/// The vault address is read from the environment rather than from the merged
/// configuration, so it cannot be satisfied from a committed <c>appsettings*.json</c> by
/// accident. The deployment sets <c>KeyVault__KeyVaultUrl</c>.
/// </remarks>
public sealed class AzureKeyVaultSecretGateway : IKeyVaultSecretGateway
{
    private const string SectionName = "KeyVault";
    private const string UrlKey = "KeyVaultUrl";
    private const string ContentType = "application/json";

    private readonly SecretClient? _client;
    private readonly ILogger<AzureKeyVaultSecretGateway> _logger;

    public AzureKeyVaultSecretGateway(
        ILogger<AzureKeyVaultSecretGateway> logger)
    {
        _logger = logger;
        _client = TryCreateClient(logger);
    }

    // Internal seam for tests that need a client they control.
    internal AzureKeyVaultSecretGateway(
        SecretClient? client,
        ILogger<AzureKeyVaultSecretGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<KeyVaultSecretRead> TryReadAsync(
        string secretName,
        CancellationToken cancellationToken)
    {
        if (_client == null)
        {
            return KeyVaultSecretRead.Unavailable;
        }

        try
        {
            var secret = await _client.GetSecretAsync(
                secretName,
                cancellationToken: cancellationToken);

            return string.IsNullOrWhiteSpace(secret?.Value?.Value)
                ? KeyVaultSecretRead.NotFound
                : KeyVaultSecretRead.Found(secret.Value.Value);
        }
        catch (RequestFailedException exception)
            when (exception.Status == 404)
        {
            return KeyVaultSecretRead.NotFound;
        }
        catch (RequestFailedException exception)
        {
            // Anything else - 403 because the identity lacks the grant, a transport failure,
            // a throttle - is "could not ask". Treating it as absent would authorise a write
            // over a ring that may well exist.
            _logger.LogWarning(
                exception,
                "Reading the payment key ring secret failed Status={Status}",
                exception.Status);

            return KeyVaultSecretRead.Unavailable;
        }
    }

    public async Task<bool> TrySetAsync(
        string secretName,
        string value,
        CancellationToken cancellationToken)
    {
        if (_client == null)
        {
            return false;
        }

        try
        {
            var secret = new KeyVaultSecret(secretName, value)
            {
                Properties = { ContentType = ContentType }
            };

            await _client.SetSecretAsync(secret, cancellationToken);

            return true;
        }
        catch (RequestFailedException exception)
        {
            _logger.LogError(
                exception,
                "Writing the payment key ring secret failed Status={Status}",
                exception.Status);

            return false;
        }
    }

    /// <summary>
    /// Returns null rather than throwing when the environment is not configured. Throwing
    /// here would take the whole service down at startup over a feature that is meant to
    /// degrade to the manual script.
    /// </summary>
    private static SecretClient? TryCreateClient(ILogger logger)
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var vaultConfiguration = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        configuration.GetSection(SectionName).Bind(vaultConfiguration);

        if (!vaultConfiguration.TryGetValue(UrlKey, out var url) ||
            string.IsNullOrWhiteSpace(url))
        {
            logger.LogWarning(
                "Payment key ring provisioning is unavailable Reason=key_vault_url_not_configured Variable={Variable}",
                $"{SectionName}__{UrlKey}");

            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var vaultUri))
        {
            logger.LogError(
                "Payment key ring provisioning is unavailable Reason=key_vault_url_malformed Variable={Variable}",
                $"{SectionName}__{UrlKey}");

            return null;
        }

        return new SecretClient(vaultUri, new DefaultAzureCredential());
    }
}

/// <summary>
/// Stands in wherever the vault cannot be written: an on-premise deployment, or a vault type
/// this build does not know. Refusing plainly beats guessing at a write path.
/// </summary>
public sealed class UnavailableKeyVaultSecretGateway : IKeyVaultSecretGateway
{
    public Task<KeyVaultSecretRead> TryReadAsync(
        string secretName,
        CancellationToken cancellationToken) =>
        Task.FromResult(KeyVaultSecretRead.Unavailable);

    public Task<bool> TrySetAsync(
        string secretName,
        string value,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
