using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Iam.DomainService.Users.RequestModel
{
    public class GetSignUpSettingRequest : IProjectKey
    {
        public string? ItemId { get; set; }
        public string ProjectKey { get ; set ; }
    }
}
