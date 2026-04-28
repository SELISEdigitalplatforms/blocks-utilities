using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Shared.Dtos
{
    public class UpdateResourceUsageCommand_Identifier
    {
        public string TenantId { get; set; }
        public string Resource { get; set; }
        public int Amount { get; set; } = 1;
    }
}
