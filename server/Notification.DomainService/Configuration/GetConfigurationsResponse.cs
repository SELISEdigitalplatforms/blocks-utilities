using Blocks.Genesis;
using DomainService.Entities;

namespace DomainService.Configuration
{
    public class GetConfigurationsResponse : BaseResponse
    {
        public long TotalCount { get; set; }
        public List<NotificationConfiguration> Configurations { get; set; } 
    }
}
