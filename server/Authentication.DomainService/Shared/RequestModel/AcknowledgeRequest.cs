namespace DomainService.Shared.RequestModel
{
    public class AcknowledgeRequest
    {
        public string ClientId { get; set; }
        public string RedirectUri { get; set; }
        public string Scope { get; set; }
        public string State { get; set; }
        public string Nonce { get; set; }
        public bool IsAcknowledged { get; set; }
        public string Username { get; set; }
    }
}
