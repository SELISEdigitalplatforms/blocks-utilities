namespace Iam.DomainService.Entities
{
    public class ResourceTimeline<CT> : BaseTimeline<CT>
    {
        public required string Entity { get; set; }
    }
}
