using Blocks.Genesis;
using Cloud.LmtService.Models.Trace;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Services.Trace
{
    public interface ITraceService
    {
        Task<BaseQueryListResponse<IQueryable<TraceProjection>>> GetTracesAsync(GetTracesRequest request);
        Task<BaseQueryListResponse<IQueryable<SingleTraceProjection>>> GetTraceAsync(GetTraceRequest request);
        Task<object> GetServiceAnalytics(GetHttpStatusAnalyticsRequest request);
        Task<object> GetOperationalAnalytics(GetApiAnalyticsRequest request);
    }
}
