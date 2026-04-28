using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.People
{
    public class TransferOwnershipRequest
    {
        public string TenantGroupId { get; set; }
        public string TransferToUserEmail { get; set; }
    }
}
