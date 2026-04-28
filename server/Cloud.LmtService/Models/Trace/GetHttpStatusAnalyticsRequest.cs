using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Models.Trace
{
    public class GetHttpStatusAnalyticsRequest : IProjectKey
    {
        public required DateTime StartTime { get; set; }
        public required DateTime EndTime { get; set; }
        public string? ServiceName { get; set; }
        public string? ProjectKey { get; set; }
    }
}
