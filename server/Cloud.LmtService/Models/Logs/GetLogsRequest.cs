using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Models.Logs
{
    public class GetLogsRequest : BaseGetsRequest<GetLogsRequestFilter>, IProjectKey
    {
        public string? Search { get; set; }
        public required string ServiceName { get; set; }
        public string? ProjectKey { get; set; }
    }

    public class GetLogsRequestFilter
    {
        public DateTime? StartDate { get; set; } = null;
        public DateTime? EndDate { get; set; } = null;
        public string? Level { get; set; }
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
    }

    public class GetLogsResponse : BaseQueryListResponse<IQueryable<object>>
    {

    }
    public class GetRestoredLogsByTraceRequest : BaseGetsRequest<object>, IProjectKey
    {
        public required string RequestId { get; set; }
        public required string TraceId { get; set; }
        public string? SpanId { get; set; }
        public string? Level { get; set; }
        public string? ProjectKey { get; set; }
    }
}
