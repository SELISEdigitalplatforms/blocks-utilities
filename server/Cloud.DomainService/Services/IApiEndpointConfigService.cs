using Blocks.Genesis;
using Cloud.DomainService.Requests;
using Cloud.DomainService.Responses;

namespace Cloud.DomainService.Services
{
    public interface IApiEndpointConfigService
    {
        Task<GetApiEndpointConfigsResponse> GetListAsync(GetApiEndpointConfigsRequest request);
        Task<BaseResponse> UpdateAsync(UpdateApiEndpointConfigRequest request);
        Task<BaseResponse> BulkUpdateAsync(BulkUpdateApiEndpointConfigRequest request);
    }
}
