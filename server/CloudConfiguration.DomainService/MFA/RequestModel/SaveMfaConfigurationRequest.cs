using Blocks.Genesis;
using CloudConfiguration.DomainService.MFA.Entities;
using CloudConfiguration.DomainService.MFA.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudConfiguration.DomainService.MFA.RequestModel
{
    public class SaveMfaConfigurationRequest : IProjectKey
    {
        public bool EnableMfa { get; set; }
        public List<CloudConfigurationUserMfaType> UserMfaType { get; set; }
        public MfaTemplate? MfaTemplate { get; set; }
        public string ProjectKey { get; set; }
    }

}
