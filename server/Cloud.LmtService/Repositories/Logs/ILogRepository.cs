using Cloud.LmtService.Models.Logs;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Repositories.Logs
{
    public interface ILogRepository
    {
        Task<IQueryable<LogProjection>> GetLogs(LiveLogRequest query);
        Task<(IQueryable<LogProjection>, long)> GetLogs(GetLogsRequest query);
        Task<(IQueryable<LogProjection>, long)> GetLogs(LogsByDateRequest request);
    }
}
