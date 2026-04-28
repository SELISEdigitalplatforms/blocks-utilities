using Blocks.Genesis;

namespace Iam.DomainService.Entities
{
    public class BaseTimeline<CT> : BaseEntity
    {
        public CT? CurrentData { get; set; }
        public string Event {  get; set; }
    }
}
