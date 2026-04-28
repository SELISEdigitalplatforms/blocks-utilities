using Blocks.Genesis;
using DomainService.Projects;
using Microsoft.Extensions.Logging;

namespace DomainService.Certificate
{
    public class CertificateStorageFactory : ICertificateStorageFactory
    {
        private readonly ILogger<CertificateStorageFactory> _logger;
        private readonly IProjectRepository _projectRepository;

        public CertificateStorageFactory(ILogger<CertificateStorageFactory> logger, IProjectRepository projectRepository)
        {
            _logger = logger;
            _projectRepository = projectRepository;
        }
        public ICertificateStorage Create(CertificateStorageType storageType)
        {
            return storageType switch
            {
                CertificateStorageType.Azure => new AzureKeyVaultStorage(_logger),
                CertificateStorageType.Filefilesystem => new LocalSystemStorage(_logger, _projectRepository),
                CertificateStorageType.Mongodb => new MongoDBStorage(_logger, _projectRepository),
                _ => throw new ArgumentException("Invalid storage type", nameof(storageType))
            };
        }
    }
}
