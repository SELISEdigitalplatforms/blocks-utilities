using Iam.DomainService.Enums;

namespace Iam.DomainService.Dtos
{
    public class ResourceMutationEvent
    {
        public required string ItemId { get; set; }
        public required MutationEventType Action { get; set; }
        public required ResourceEntity Entity { get; set; }
    }

    
}
