using Blocks.Genesis;
namespace Utility.DomainService.Sequence
{
    public class SequenceNumberQuery : IProjectKey
    {
        public string Context { get; set; } = string.Empty;
        public string? ProjectKey { get; set; }
    }
    public class SequenceNumberQueryResponse : BaseResponse
    {
        public string Context { get; set; } = string.Empty;
        public long CurrentNumber { get; set; }
    }
}