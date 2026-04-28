using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Shared.RequestModel
{
    public class GenerateUserCodeRequest
    {
        public string ClientId { get; set; }
        public int CodeTtlInMinute { get; set; } = 10080; // default 7 days 
        public string Note { get; set; }
    }
}
