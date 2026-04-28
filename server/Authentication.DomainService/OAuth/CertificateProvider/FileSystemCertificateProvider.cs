
using Microsoft.Extensions.Logging;

namespace DomainService.OAuth
{
    public class FileSystemCertificateProvider : ICertificateProvider
    {
        private readonly ILogger _logger;

        public FileSystemCertificateProvider(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<byte[]> GetCertificateAsync(string key)
        {
            try
            {
                var path = "";
                return await File.ReadAllBytesAsync(path);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error retrieving certificate from file system");
                return Array.Empty<byte>();
            }
        }
    }
}
