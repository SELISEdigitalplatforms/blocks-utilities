using System.Security.Cryptography.X509Certificates;

namespace DomainService.Certificate
{
    public interface ICertificateStorage
    {
        Task UploadCertificateAsync(X509Certificate2 certificate, string password, string certificateName);
    }
}
