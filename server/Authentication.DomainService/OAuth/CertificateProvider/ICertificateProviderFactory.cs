using Blocks.Genesis;

namespace DomainService.OAuth
{
    public interface ICertificateProviderFactory
    {
        ICertificateProvider GetProvider(CertificateStorageType providerType);
    }
}
