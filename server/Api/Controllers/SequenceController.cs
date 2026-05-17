using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Utility.DomainService.Sequence.service;
using Utility.DomainService.Sequence;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class SequenceController : ControllerBase
    {
        
        private readonly ISequenceService _sequenceService;
        /// <summary>
        /// Initializes a new instance of the <see cref="SequenceController"/> class.
        /// </summary>
        /// <param name="sequenceService">The sequence service.</param>
        public SequenceController(ISequenceService sequenceService)
        {
            _sequenceService = sequenceService;
        }

        /// <summary>
        /// Query request to get the next number in sequence.
        /// </summary>
        /// <remarks> <param name="query"></param> List of recipients and additional information:
        /// 
        /// command. Context: Context for the query request.
        /// 
        /// </remarks>
        /// 
        /// <returns></returns>
        [HttpGet]
        [Authorize]
        public Task<SequenceNumberQueryResponse> Next([FromQuery] SequenceNumberQuery query)
        {
            return _sequenceService.GetNextSequenceNumberAsync(query);
        }

        /// <summary>
        /// Query request to get 63 billion unique sequence of numbers in hexadecimal format
        /// </summary>
        /// <remarks> <param name="query"></param> List of recipients and additional information:
        /// 
        /// command. Context: Context for the query request.
        /// 
        /// </remarks>
        /// 
        /// <returns></returns>
        [HttpGet]
        [Authorize]
        public Task<SequenceNumberHexQueryResponse> NextHex([FromQuery] SequenceNumberHexQuery query)
        {
            return _sequenceService.GetNextHexSequenceNumberAsync(query);
        }

        /// <summary>
        /// Reset the sequence. Sequence will begin again from a user-specified number.
        /// </summary>
        /// <remarks> <param name="request"></param> List of recipients and additional information:
        /// 
        /// command. Context: Context for the query request.
        /// 
        /// command. Value: Specify the starting number for sequence once it is reset.
        /// 
        /// </remarks>
        /// 
        /// <returns></returns>
        [HttpPost]
        [Authorize]
        public Task<BaseResponse> Reset([FromBody] ResetSequenceNumberRequest request)
        {
            return _sequenceService.ResetSequenceNumberAsync(request);
        }
    }
}