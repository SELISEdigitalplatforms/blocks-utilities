using Blocks.Genesis;
using Captcha.DomainService.Utilities;

namespace Captcha.DomainService.Captcha;

public class CaptchaProcessor : ICaptchaProcessor
{
    private readonly ICacheClient _cache;
    private readonly ICaptchaGeneratorProvider _captchaGeneratorProvider;
    private readonly IContextCaptchaIdGeneratorService _contextCaptchaIdGeneratorService;
    private readonly ICaptchaVerificationServiceProvider _captchaVerificationServiceProvider;
    public CaptchaProcessor(ICacheClient cache,
            ICaptchaGeneratorProvider captchaGeneratorProvider,
            IContextCaptchaIdGeneratorService contextCaptchaIdGeneratorService,
            ICaptchaVerificationServiceProvider captchaVerificationServiceProvider)
    {
        _cache = cache;
        _captchaGeneratorProvider = captchaGeneratorProvider;
        _contextCaptchaIdGeneratorService = contextCaptchaIdGeneratorService;
        _captchaVerificationServiceProvider = captchaVerificationServiceProvider;
    }

    public CaptchaInformation GetCaptchaInformation(string provider)
    {
        var contextCaptchaId = _contextCaptchaIdGeneratorService.GetContextCaptchaId();
        var captchaValue = Path.GetRandomFileName().Substring(0, 4);
        var captchaBase64String = _captchaGeneratorProvider.GetCaptchaGenerator(provider).Generate(captchaValue);
        _cache.AddStringValue(contextCaptchaId, captchaValue, 10 * 60);

        return new CaptchaInformation
        {
            Id = contextCaptchaId,
            Captcha = captchaBase64String
        };
    }

    public async Task<string> SubmitAndCreateVerificationCodeAsync(string captchaId)
    {
        var verificationCode = Guid.NewGuid().ToString("n");
        var hostName = "abc.com";
        await _cache.AddStringValueAsync(verificationCode, hostName, 10 * 60);
        await _cache.RemoveKeyAsync(captchaId);

        return verificationCode;
    }

    public async Task<VerificationResult> VerifyCaptchaAsync(string configProvider, string verificationCode)
    {
        var captchaVerificationHandler = _captchaVerificationServiceProvider.GetCaptchaVerificationService(configProvider);
        return await captchaVerificationHandler.VerifyAsync(verificationCode);
    }
}
