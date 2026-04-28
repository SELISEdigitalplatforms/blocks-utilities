using Iam.DomainService.Enums;

namespace Iam.DomainService.Dtos
{
    public record UserMutationEvent
    {
        public required string ItemId { get; set; }
        public required MutationEventType Action { get; set; }
    }
}
