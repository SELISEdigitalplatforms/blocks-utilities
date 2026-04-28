using Blocks.Genesis;
using Cloud.DomainService.Models;
using Cloud.DomainService.Repositories;
using Cloud.DomainService.Requests;
using Cloud.DomainService.Responses;

namespace Cloud.DomainService.Services
{
    public class ApiEndpointConfigService : IApiEndpointConfigService
    {
        private readonly IApiEndpointConfigRepository _repository;

        public ApiEndpointConfigService(IApiEndpointConfigRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetApiEndpointConfigsResponse> GetListAsync(GetApiEndpointConfigsRequest request)
        {
            var (data, count) = await _repository.GetListAsync(request);

            return new GetApiEndpointConfigsResponse
            {
                Data = data.AsQueryable(),
                TotalCount = count,
                Page = request.Page,
                PageSize = request.PageSize
            };
        }

        public async Task<BaseResponse> UpdateAsync(UpdateApiEndpointConfigRequest request)
        {
            var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;
            var success = await _repository.UpdateAsync(request.ProjectKey, request.ItemId,request.IsCaptchaRequired,request.IsMfaRequired,userId);

            return new BaseResponse
            {
                IsSuccess = success,
                Errors = success
                    ? []
                    : new Dictionary<string, string> { { "update_failed", "No matching record found to update" } }
            };
        }

        public async Task<BaseResponse> BulkUpdateAsync(BulkUpdateApiEndpointConfigRequest request)
        {
            var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;

            var isCaptchaRequired = request.DisableAll ? false : request.IsCaptchaRequired;
            var isMfaRequired = request.DisableAll ? false : request.IsMfaRequired;

            var modifiedCount = await _repository.BulkUpdateAsync(
                request.ProjectKey,
                request.ItemIds,
                isCaptchaRequired,
                isMfaRequired,
                userId);

            var success = modifiedCount > 0;

            return new BaseResponse
            {
                IsSuccess = success,
                Errors = success
                    ? []
                    : new Dictionary<string, string> { { "update_failed", "No matching records found to update" } }
            };
        }
    }
}
