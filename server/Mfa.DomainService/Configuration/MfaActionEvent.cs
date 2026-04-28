namespace Mfa.DomainService.Configuration
{
    public class MfaActionEvent
    {
        public required bool IsEnable { get; set; }
        public required string ProjectKey { get; set; }
    }
}
