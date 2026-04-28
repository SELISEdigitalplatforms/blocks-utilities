
namespace DomainService.OAuth
{
    public interface ICertificateProvider
    {
        Task<byte[]> GetCertificateAsync(string key);
    }
}
