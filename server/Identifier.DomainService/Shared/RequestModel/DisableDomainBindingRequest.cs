using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Shared.Entities
{
    public class DisableDomainBindingRequest
    {
        public string ProjectId { get; set; }
        public string Domain {  get; set; }
    }
}
