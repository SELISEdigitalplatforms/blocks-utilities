namespace DomainService.Projects
{
    public class UpdateAuthConfigRequest
    {
        public int RefreshTokenValidForNumberMinutes { get; set; }
        public int GetNumberOfWrongAttemptsToLockTheAccount { get; set; }
        public int AccountLockDurationInMinutes { get; set; }
        public string ProjectId { get; set; }
        public List<string> AllowedGrantTypes { get; set; }
    }
}
