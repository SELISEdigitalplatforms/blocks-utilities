using Blocks.Genesis;

namespace CloudConfiguration.DomainService.Authentication
{
    public class UpdateAuthenticationConfigurationRequest : IProjectKey
    {
        public string ItemId { get; set; }
        public int RefreshTokenValidForNumberMinutes { get; set; }
        public int GetNumberOfWrongAttemptsToLockTheAccount { get; set; }
        public int AccountLockDurationInMinutes { get; set; }
        public int AccessTokenValidForNumberMinutes { get; set; }
        public int RememberMeRefreshTokenValidForNumberMinutes { get; set; }
        public List<string> AllowedGrantTypes { get; set; }
        public string? ProjectKey { get; set; }
    }
}
