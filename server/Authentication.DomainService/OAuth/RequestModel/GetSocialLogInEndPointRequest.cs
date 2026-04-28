namespace DomainService.OAuth.RequestModel
{
    public record GetSocialLogInEndPointRequest
    {
        public required string Provider { get; set; }
        public required string Audience { get; set; }
        public string? NextUrl { get; set; }
        public bool SendAsResponse { get; set; } = true;
    }

    public class GetSocialLogInEndPointResponse
    {
        public bool IsAResponse { get; set; }
        public string ProviderUrl { get; set; }
        public string Error { get; set; }
    }
}
