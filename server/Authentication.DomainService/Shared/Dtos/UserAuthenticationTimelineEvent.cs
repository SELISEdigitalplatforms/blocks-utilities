using Iam.DomainService.Dtos;

namespace DomainService.Dtos
{
    public class UserAuthenticationTimelineEvent
    {
        public DeviceInformation? DeviceInformation { get; set; }
        public string IpAddresses { get; set; }
        public string Event { get; set; }
        public string ActionBy { get; set; }
        public string UserId { get; set; }
    }
}
