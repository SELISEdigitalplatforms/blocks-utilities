using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Models.Trace
{
    public class GetTracesRequest : BaseGetsRequest<GetTracesRequestFilter>, IProjectKey
    {
        public string? Search { get; set; }
        public string? ProjectKey { get; set; }
    }

    public class GetTracesRequestFilter
    {
        public DateTime? StartDate { get; set; } = null;
        public DateTime? EndDate { get; set; } = null;
        public List<string> Services { get; set; } = new List<string>();
        public List<string> Excepts { get; set; } = new List<string>();
        public List<int> StatusCodes { get; set; } = new List<int>();
    }
    public class GetTracesResponse : BaseQueryListResponse<IQueryable<object>>
    {

    }
}
