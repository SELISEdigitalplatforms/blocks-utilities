using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Iam.DomainService.Resources.RequestModel
{
    public class GetPermissionGroupBySeverityRequest : IProjectKey
    {
        public string? ProjectKey { get ; set ; }
    }
}
