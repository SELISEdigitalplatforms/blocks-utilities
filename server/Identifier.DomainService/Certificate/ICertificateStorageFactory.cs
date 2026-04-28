using Blocks.Genesis;

namespace DomainService.Certificate
{
    public interface ICertificateStorageFactory
    {
        ICertificateStorage Create(CertificateStorageType storageType);
    }
}
