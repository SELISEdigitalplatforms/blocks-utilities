using Blocks.Genesis;
using Cloud.DomainService.Models;

namespace Cloud.DomainService.Responses
{
    public class GetApiEndpointConfigsResponse : BaseQueryListResponse<IQueryable<ApiEndpointConfigResponse>>
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public long TotalPages => PageSize > 0 ? (long)Math.Ceiling((double)TotalCount / PageSize) : 0;
    }
    public class ApiEndpointConfigResponse : ApiEndpointConfig
    {
        public string? Controller { get; set; }
        public string? Method { get; set; }
        public string? Service { get; set; } = string.Empty;
    }
}
