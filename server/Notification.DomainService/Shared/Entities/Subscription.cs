using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.Shared
{
    public class Subscription
    {
        public Subscription()
        {
            Payload = new NotifierPayload();
        }
        public NotifierPayload Payload { get; set; }
    }
}
