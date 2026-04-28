using Blocks.Genesis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Cloud.LmtService.Models.Logs
{
    public class LiveLogRequest : IProjectKey
    {
        public required string Name { get; set; }
        public DateTime LastDate { get; set; }
        public string? ProjectKey { get; set; }
    }
}
