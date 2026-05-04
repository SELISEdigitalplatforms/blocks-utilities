using Blocks.Genesis;
namespace Utility.DomainService.Sequence
{
    public class SequenceNumberHexQuery : IProjectKey
    {
        public string Context { get; set; } = string.Empty;
        public string? ProjectKey { get; set; }
    }
    public class SequenceNumberHexQueryResponse : BaseResponse
    {
        public string Context { get; set; } = string.Empty;
        public string CurrentNumber { get; set; } = string.Empty;
    }
}