using Blocks.CaptchaDriver;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using Captcha.DomainService.Utilities;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Blocks.Extension.DependencyInjection;

public static class CaptchaDriverServiceExtension
{
    public static void RegisterBlocksCaptchaService(this IServiceCollection services)
    {
        // Register validator
        services.AddTransient<IValidator<CreateCaptchaRequest>, CreateCaptchaCommandValidator>();
        services.AddTransient<IValidator<SubmitCaptchaRequest>, SubmitCaptchaCommandValidator>();

        // Register services
        services.AddSingleton<ICaptchaService, CaptchaService>();
        services.AddSingleton<ICaptchaConfigurationService, CaptchaConfigurationService>();
        services.AddSingleton<ICaptchaConfigurationRepository, CaptchaConfigurationRepository>();
        services.AddSingleton<ICaptchaGeneratorProvider, CaptchaGeneratorProvider>();
        services.AddSingleton<IContextCaptchaIdGeneratorService, ContextCaptchaIdGeneratorService>();
        services.AddSingleton<ICaptchaVerificationServiceProvider, CaptchaVerificationServiceProvider>();
        services.AddSingleton<ICaptchaProcessor, CaptchaProcessor>();
        services.AddSingleton<ICaptchaDriverService, CaptchaDriverService>();
        services.AddSingleton<IRecaptchaConfigFactory, RecaptchaConfigFactory>();
        services.AddSingleton<IHttpClientService, HttpClientService>();
        services.AddSingleton<ReCaptchaVerificationService>();
    }
}
