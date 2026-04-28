namespace DomainService.Dtos
{
    public class RefreshTokenCache
    {
        public string RefreshToken { get; set; }
        public string TenantId { get; set; }
        public DateTime IssuedUtc { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public string UserId { get; set; }
        public string IpAddresses { get; set; }
    }
}
