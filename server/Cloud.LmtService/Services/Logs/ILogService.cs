using Cloud.LmtService.Models.Logs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Services.Logs
{
    public interface ILogService
    {
        Task<GetLogsResponse> GetLogsAsync(GetLogsRequest request);
        Task<GetLogsResponse> GetLiveLogsAsync(LiveLogRequest request);
        Task<GetLogsResponse> GetLogsByDateAsync(LogsByDateRequest request);
    }
}
