using Blocks.Genesis;

namespace DomainService.Entities
{
    
    public class ResourceLimit : BaseEntity
    {
        public string Resource { get; set; }
        public string ResourceType { get; set; }
        public int Limit { get; set; }
        public int Usage { get; set; }
        public DateTime Lifetime { get; set; }
        public bool IsActive { get; set; }
        public bool EnableAutoRenew { get; set; }
        public string TenantId { get; set; }
        public string Type { get; set; }
    }
}
