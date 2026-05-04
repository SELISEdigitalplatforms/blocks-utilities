using Blocks.Genesis;

namespace Utility.DomainService.Sequence.service
{
    public class SequenceService : ISequenceService
    {
        private readonly ISequenceRepository _sequenceRepository;

        public SequenceService(ISequenceRepository sequenceRepository)
        {
            _sequenceRepository = sequenceRepository;
        }

        public async Task<SequenceNumberQueryResponse> GetNextSequenceNumberAsync(SequenceNumberQuery query)
        {
            var nextNumber = await _sequenceRepository.GetNextSequenceNumberAsync(query.Context);
            return new SequenceNumberQueryResponse
            {
                Context = query.Context,
                CurrentNumber = nextNumber,
                IsSuccess = true
            };
        }

        public async Task<SequenceNumberHexQueryResponse> GetNextHexSequenceNumberAsync(SequenceNumberHexQuery query)
        {
            var nextNumber = await _sequenceRepository.GetNextHexSequenceNumberAsync(query.Context);
            return new SequenceNumberHexQueryResponse
            {
                Context = query.Context,
                CurrentNumber = string.Format("{0:x9}", nextNumber).ToUpper(),
                IsSuccess = true
            };
        }

        public async Task<BaseResponse> ResetSequenceNumberAsync(ResetSequenceNumberRequest request)
        {
            await _sequenceRepository.ResetSequenceNumberAsync(request.Context, request.Value);
            return new BaseResponse
            {
                IsSuccess = true
            };
        }
    }
}