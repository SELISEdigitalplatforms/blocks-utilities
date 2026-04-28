using Cloud.DomainService.Models;
using Cloud.DomainService.Requests;
using Cloud.DomainService.Responses;

namespace Cloud.DomainService.Repositories
{
    public interface IApiEndpointConfigRepository
    {
        Task<(List<ApiEndpointConfigResponse>, long)> GetListAsync(GetApiEndpointConfigsRequest request);
        Task<bool> UpdateAsync(string projectKey,string itemId, bool isCaptchaRequired, bool isMfaRequired, string updatedBy);
        Task<long> BulkUpdateAsync(string projectKey, List<string> itemIds, bool isCaptchaRequired, bool isMfaRequired, string updatedBy);
    }
}
