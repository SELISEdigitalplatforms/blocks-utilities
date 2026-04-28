
namespace CloudConfiguration.DomainService.MFA.Entities
{
    public class MfaActionEvent
    {
        public required bool IsEnable { get; set; }
        public required string ProjectKey { get; set; }
    }
}
