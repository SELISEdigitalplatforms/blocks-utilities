namespace DomainService.OAuth
{
    public record StateInfo
    {
        public required string Provider { get; set; }
        public string Code { get; set; }
        public string NextUrl { get; set; }
        public required string Audience { get; set; }
        public string? State { get; set; }
        public string? Scope { get; set; }
        public string? UserName { get; set; }
        public string? Nonce { get; set; }
        public Dictionary<string, string> Extra { get; set; }

    }
}
