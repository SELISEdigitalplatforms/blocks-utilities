
namespace DomainService.Shared
{
    public class NotifyRequest : NotifierPayload
    {
        public string ResponseKey { get; set; }
        public string ResponseValue { get; set; }
        public bool ContentAvailable { get; set; }
        public string ConfigurationName { get; set; }
    }
}
