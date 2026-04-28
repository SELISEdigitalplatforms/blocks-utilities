namespace Captcha.DomainService.Captcha
{
    public class VerificationResult
    {
        public VerificationResult()
        {
            Verified = false;
            HostName = "";
        }
        public bool Verified { get; set; }
        public string HostName { get; set; }
        public IDictionary<string, string>? Errors { get; set; }

        public VerifyCaptchaRequestResponse ToVerifyCaptchaQueryResponse()
        {
            return new VerifyCaptchaRequestResponse
            {
                Errors = Errors,
                HostName = HostName,
                Verified = Verified,
                IsSuccess = true
            };
        }
    }
}
