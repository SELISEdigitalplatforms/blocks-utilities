namespace Iam.DomainService.Dtos
{
    public class RefreshTokenEvent
    {
        public string RefreshToken { get; set; }
        public string TenantId { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string UserId { get; set; }
        public string IpAddresses { get; set; }
        public DeviceInformation? DeviceInformation { get; set; }
        public bool IsLogin { get; set; }
        public bool IsRevoke { get; set; }
        public string? GrantType { get; set; }
    }
}