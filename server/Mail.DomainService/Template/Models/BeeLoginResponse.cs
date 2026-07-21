using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Mail.DomainService.Template.Models
{
    public sealed class BeeLoginResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }
}
