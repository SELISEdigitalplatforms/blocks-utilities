using Blocks.Genesis;

namespace CloudConfiguration.DomainService.IAM.RequestModel
{
    public class SaveIamConfigurationRequest : IProjectKey
    {
        public string AccountActivationUrl { get; set; }
        public string AccountVerificationUrl { get; set; }
        public string RecoverAccountUrl { get; set; }
        public int ActivationUrlLifetimeInMinutes { get; set; } = 60 * 24; // Default 1 day
        public int RecoverAccountUrlLifetimeInMinutes { get; set; } = 10; // Default 10 mins
        public bool LogoutOnPasswordChange { get; set; } = true;
        public string PasswordStrengthCheckerRegex { get; set; } = "^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[\\W_])[A-Za-z\\d\\W_]{8,30}$";
        public string? ProjectKey { get; set; }
    }
}
