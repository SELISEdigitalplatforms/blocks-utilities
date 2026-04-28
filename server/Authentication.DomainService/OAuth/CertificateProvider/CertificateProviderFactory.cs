
using Blocks.Genesis;
using Microsoft.Extensions.Logging;

namespace DomainService.OAuth
{
    public class CertificateProviderFactory : ICertificateProviderFactory
    {
        private readonly ILogger<CertificateProviderFactory> _logger;
        private readonly IBlocksSecret _blocksSecret;

        public CertificateProviderFactory(ILogger<CertificateProviderFactory> logger, IBlocksSecret blocksSecret)
        {
            _logger = logger;
            _blocksSecret = blocksSecret;
        }

        public ICertificateProvider GetProvider(CertificateStorageType providerType)
        {
            return providerType switch
            {
                CertificateStorageType.Azure => new AzureVaultCertificateProvider(_logger),
                CertificateStorageType.Filefilesystem => new FileSystemCertificateProvider(_logger),
                CertificateStorageType.Mongodb => new MongodbCertificateProvider(_logger, _blocksSecret),
                _ => throw new ArgumentException($"Unsupported provider type: {providerType}")
            };
        }
    }
}
