using Blocks.Genesis;
using Iam.DomainService.Dtos;

namespace DomainService.Entities
{
    public class UserAuthenticationTimeline : BaseEntity
    {
        public string Event { get; set; }
        public string ActionBy { get; set; }
        public string IpAddresses { get; set; }
        public DeviceInformation? DeviceInformation { get; set; }
    }
}
