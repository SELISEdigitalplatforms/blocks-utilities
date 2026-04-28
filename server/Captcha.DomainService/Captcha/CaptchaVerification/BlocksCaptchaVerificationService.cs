using Blocks.Genesis;

namespace Captcha.DomainService.Captcha
{
    public class BlocksCaptchaVerificationService : ICaptchaVerificationService
    {
        private readonly ICacheClient _cache;

        public BlocksCaptchaVerificationService(ICacheClient cache)
        {
            _cache = cache;
        }

        public Task<string> ResolveVerificationUri(string token)
        {
            throw new NotImplementedException();
        }

        public async Task<VerificationResult> VerifyAsync(string verificationCode)
        {
            var savedHostName = await _cache.GetStringValueAsync(verificationCode);
            var verified = !string.IsNullOrWhiteSpace(savedHostName);

            if (!verified)
            {
                return new VerificationResult
                {
                    Verified = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "VerificationCode", "Verification code incorrect or expire." }
                    }
                };
            }

            await _cache.RemoveKeyAsync(verificationCode);

            return new VerificationResult
            {
                Verified = true,
                HostName = savedHostName
            };
        }

        public Task<RecaptchaResponse> VerifyCaptchaAsync(string token)
        {
            throw new NotImplementedException();
        }
    }
}
