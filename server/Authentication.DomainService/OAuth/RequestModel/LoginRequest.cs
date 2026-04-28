namespace DomainService.OAuth.RequestModel
{
    public class LoginRequest
    {
        public string Username { get; set; }
        public string? Password { get; set; }
        public string ClientId { get; set; }
        public string RedirectUri { get; set; }
        public string Scope { get; set; }
        public string State { get; set; }
        public string Nonce { get; set; }
    }
}
