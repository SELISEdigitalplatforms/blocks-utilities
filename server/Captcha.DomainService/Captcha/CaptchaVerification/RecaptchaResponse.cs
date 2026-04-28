using Newtonsoft.Json;

namespace Captcha.DomainService.Captcha
{
    public class RecaptchaResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }
        [JsonProperty("hostname")]
        public string HostName { get; set; }
    }
}
