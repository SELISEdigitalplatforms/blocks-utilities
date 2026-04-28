using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Iam.DomainService.Shared.Dtos
{
    public class UserStatusChangedEvent
    {
        public string UserId { get; set; }
        public bool IsActive { get; set; }
        public string ApiKey { get; set; }
    }
}
