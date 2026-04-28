using Cloud.LmtService.Models.Trace;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Repositories.Trace
{
    public interface ITraceRepository
    {
        Task<IQueryable<SingleTraceProjection>> GetTraces(GetTraceRequest query);
        Task<(IQueryable<TraceProjection>, long)> GetTraces(GetTracesRequest query);
        Task<object> GetOperationalAnalytics(DateTime startTime, DateTime endTime, string serviceName, string? operationSearch = null);
        Task<object> GetServiceAnalytics(DateTime startTime, DateTime endTime, string? serviceName = null);
    }
}
