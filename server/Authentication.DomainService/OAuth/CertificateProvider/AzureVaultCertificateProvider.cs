using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DomainService.OAuth
{
    public class AzureVaultCertificateProvider : ICertificateProvider
    {
        private readonly ILogger _logger;
        private SecretClient _secretClient;

        public AzureVaultCertificateProvider(ILogger logger)
        {
            _logger = logger;
            SetupKeyVault();

            if (_secretClient == null) throw new InvalidOperationException("One or more required Azure config values are missing. Please check your environment configuration.");
        }

        public async Task<byte[]> GetCertificateAsync(string key)
        {
            try
            {
                var value = await GetSecretFromKeyVaultAsync(key);
                var certificate = Convert.FromBase64String(value);

                _logger.LogInformation("Retrieved certificate from Azure Key Vault");
                return certificate ?? Array.Empty<byte>();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error retrieving certificate from Azure Key Vault");
                return Array.Empty<byte>();
            }
        }

        public void SetupKeyVault()
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var cloudConfig = new Dictionary<string, string>();
            configuration.GetSection("KeyVault").Bind(cloudConfig);

            if (!cloudConfig.TryGetValue("KeyVaultUrl", out var keyVaultUrl) ||
                !cloudConfig.TryGetValue("TenantId", out var tenantId) ||
                !cloudConfig.TryGetValue("ClientId", out var clientId) ||
                !cloudConfig.TryGetValue("ClientSecret", out var clientSecret))
            {
                _logger.LogError("One or more required Azure config values are missing. Please check your environment configuration.");
                throw new InvalidOperationException("One or more required Azure config values are missing. Please check your environment configuration.");
            }

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _secretClient = new SecretClient(new Uri(keyVaultUrl), credential);
        }

        private async Task<string> GetSecretFromKeyVaultAsync(string key)
        {
            try
            {
                var secret = await _secretClient.GetSecretAsync(key);
                return secret.Value.Value;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error retrieving secret '{key}': {e.Message}");
                return string.Empty;
            }
        }

    }
}