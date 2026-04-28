using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Shared.Dtos
{
    public class HttpsCertificateInfo
    {
        public bool HasValidCertificate { get; set; }
        public string Subject { get; set; }
        public string Issuer { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidUntil { get; set; }
        public bool IsExpired { get; set; }
        public string SslPolicyErrors { get; set; }
    }
}
