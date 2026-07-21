using System;
using System.Collections.Generic;
using System.Text;

namespace Mail.DomainService.Template.Models
{
    public sealed class PluginRequest
    {
        public required HttpMethod Method { get; init; }

        public required string Url { get; init; }

        public object? Payload { get; init; }

        public required string ContentType { get; init; }

        public required Dictionary<string, string> Headers { get; init; }

        public bool IsFormUrlEncoded { get; init; }
    }
}
