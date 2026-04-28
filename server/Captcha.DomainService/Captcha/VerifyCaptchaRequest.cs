using Blocks.Genesis;

namespace Captcha.DomainService.Captcha
{
    public class VerifyCaptchaRequest
    {
        /// <summary>
        /// command. VerificationCode: String representing the verification code for Captcha.
        /// </summary>
        public string VerificationCode { get; set; }
        public string ConfigurationName { get; set; }
    }

    public class VerifyCaptchaRequestResponse : BaseMutationResponse
    {
        public VerifyCaptchaRequestResponse()
        {
            Verified = false;
            HostName = "";
        }

        public bool Verified { get; set; }
        public string HostName { get; set; }
    }
}
