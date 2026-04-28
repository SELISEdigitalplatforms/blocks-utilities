using Blocks.Genesis;
using System.Security.Cryptography.X509Certificates;

namespace DomainService.Certificate
{
    public interface ICertificateManager
    {
        (X509Certificate2, X509Certificate2) GenerateCertificates(JwtTokenParameters parameters);
        string GeneratePrivateCertificateName(string tenantId, string itemId);
        Task UploadPrivateCertificateAsync(CertificateStorageType storageType, X509Certificate2 certificate, string password, string certificateName);

    }
}
