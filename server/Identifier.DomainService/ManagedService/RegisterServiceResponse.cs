using Blocks.Genesis;

namespace DomainService.ManagedService
{
    public class RegisterServiceResponse : BaseResponse
    {
        public string ItemId { get; set; }
        public string ServiceId { get; set; }
        public string LogsServiceBusConnectionString { get; set; }
        public string TracesServiceBusConnectionString { get; set; }
    }
}
