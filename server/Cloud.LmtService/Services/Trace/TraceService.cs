using Blocks.Genesis;
using Cloud.LmtService.Models.Trace;
using Cloud.LmtService.Repositories.Trace;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Services.Trace
{
    public class TraceService : ITraceService
    {
        private readonly ILogger<TraceService> _logger;
        private readonly ITraceRepository _traceRepository;

        public TraceService(ILogger<TraceService> logger, ITraceRepository traceRepository)
        {
            _logger = logger;
            _traceRepository = traceRepository;
        }

        public async Task<BaseQueryListResponse<IQueryable<SingleTraceProjection>>> GetTraceAsync(GetTraceRequest request)
        {
            _logger.LogInformation("Start of GetTraceAsync");
            if (string.IsNullOrWhiteSpace(request.TraceId))
            {
                return new BaseQueryListResponse<IQueryable<SingleTraceProjection>>
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "Error", "Trace id is missing"}
                    }
                };
            }

            var result = await _traceRepository.GetTraces(request);

            return new BaseQueryListResponse<IQueryable<SingleTraceProjection>>
            {
                Data = result
            };
        }

        public async Task<BaseQueryListResponse<IQueryable<TraceProjection>>> GetTracesAsync(GetTracesRequest request)
        {
            _logger.LogInformation("Start of GetTracesAsync");
            var (result, total) = await _traceRepository.GetTraces(request);

            return new BaseQueryListResponse<IQueryable<TraceProjection>>
            {
                Data = result,
                TotalCount = total
            };
        }

        public async Task<object> GetOperationalAnalytics(GetApiAnalyticsRequest request)
        {
            _logger.LogInformation("Start of GetTracesAsync");
            var result = await _traceRepository.GetOperationalAnalytics(request.StartTime, request.EndTime, request.ServiceName, request.OperationName);

            return result;
        }

        public async Task<object> GetServiceAnalytics(GetHttpStatusAnalyticsRequest request)
        {
            _logger.LogInformation("Start of GetTracesAsync");
            var result = await _traceRepository.GetServiceAnalytics(request.StartTime, request.EndTime, request.ServiceName);

            return result;
        }

    }
}
