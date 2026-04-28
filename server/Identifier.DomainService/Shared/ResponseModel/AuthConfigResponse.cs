using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Shared
{
    public class AuthConfigResponse : BaseResponse
    {
        public string PublicCertificatePath { get; set; }
        public int CertificateValidForNumberOfDays { get; set; }
        public int RefreshTokenValidForNumberMinutes { get; set; }
        public int GetNumberOfWrongAttemptsToLockTheAccount { get; set; }
        public int AccountLockDurationInMinutes { get; set; }
        public DateTime CertificateIssueDate { get; set; }
        public List<string> AllowedGrantTypes { get; set; }
    }
}
