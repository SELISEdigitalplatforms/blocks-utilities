using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Projects;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

namespace DomainService.Certificate
{
    public class LocalSystemStorage : ICertificateStorage
    {
        private readonly ILogger _logger;
        private readonly IProjectRepository _projectRepository;

        public LocalSystemStorage(ILogger logger, IProjectRepository projectRepository)
        {
            _logger = logger;
            _projectRepository = projectRepository;
        }
        public async Task UploadCertificateAsync(X509Certificate2 certificate, string password, string certificateName)
        {
            byte[] pfxBytes = certificate.Export(X509ContentType.Pkcs12, password);
            string base64Certificate = Convert.ToBase64String(pfxBytes);

            var bcontext = BlocksContext.GetContext();

            var document = new TenantCertificate
            {
                ItemId = Guid.NewGuid().ToString(),
                Key = certificateName,
                Value = base64Certificate,
                CreatedDate = DateTime.Now,
                LastUpdatedDate = DateTime.Now,
                CreatedBy = bcontext.UserId,
                LastUpdatedBy = bcontext.UserId
            };

            await _projectRepository.SaveTenantCertificate(document);
        }
    }
}
