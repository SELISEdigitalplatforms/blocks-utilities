using Blocks.Genesis;

namespace Utility.DomainService.Sequence.service
{ 
    public interface ISequenceService
    {
        Task<SequenceNumberQueryResponse> GetNextSequenceNumberAsync(SequenceNumberQuery query);
        Task<SequenceNumberHexQueryResponse> GetNextHexSequenceNumberAsync(SequenceNumberHexQuery query);
        Task<BaseResponse> ResetSequenceNumberAsync(ResetSequenceNumberRequest request);
    }
}