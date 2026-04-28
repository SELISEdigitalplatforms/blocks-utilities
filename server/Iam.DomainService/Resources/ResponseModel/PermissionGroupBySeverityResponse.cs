using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Iam.DomainService.Resources.ResponseModel
{
    public class PermissionGroupBySeverityResponse
    {
        public string SeverityLevel { get; set; }
        public long Count { get; set; }
    }
}
