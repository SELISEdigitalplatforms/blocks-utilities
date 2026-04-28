using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

namespace DomainService.Certificate
{
    public class AzureKeyVaultStorage : ICertificateStorage
    {
        private readonly ILogger _logger;
        private SecretClient _secretClient;

        public AzureKeyVaultStorage(ILogger logger)
        {
            _logger = logger;
            SetupKeyVault();

            if (_secretClient == null) throw new Exception("One or more required Azure config values are missing. Please check your environment configuration.");
        }

        public async Task UploadCertificateAsync(X509Certificate2 certificate, string password, string certificateName)
        {
            byte[] pfxBytes = certificate.Export(X509ContentType.Pkcs12, password);
            string base64Certificate = Convert.ToBase64String(pfxBytes);

            var secret = new KeyVaultSecret(certificateName, base64Certificate);
            await _secretClient.SetSecretAsync(secret);
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
                throw new Exception("One or more required Azure config values are missing. Please check your environment configuration.");
            }

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            _secretClient = new SecretClient(new Uri(keyVaultUrl), credential);
        }
    }
}
