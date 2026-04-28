
namespace DomainService.ManagedService
{
    public class ServiceUpdateMessage
    {
        public string Action { get; set; } // "add" or "remove"
        public string ServiceId { get; set; }
    }
}
