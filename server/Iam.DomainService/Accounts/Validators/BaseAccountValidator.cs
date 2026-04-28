using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using FluentValidation;
using Iam.DomainService.Configurations;
using Iam.DomainService.Services;
using MongoDB.Driver;

namespace Iam.DomainService.Accounts
{
    public class BaseAccountValidator : PasswordValidator<BaseAccountRequest>
    {
        private readonly ICacheClient _cacheClient;
        private readonly ICaptchaService _captchaService;
        private readonly IDbContextProvider _dbContextProvider;


        public BaseAccountValidator(ICacheClient cacheClient,
                                    IIamConfigurationRepository configurationRepository,
                                    IIdentityAccessManagementRepository identityAccessManagementRepository,
                                    ICaptchaService captchaService,
                                    IDbContextProvider dbContextProvider)
            : base(identityAccessManagementRepository, configurationRepository)
        {
            _cacheClient = cacheClient;
            _captchaService = captchaService;
            _dbContextProvider = dbContextProvider;

            RuleFor(u => u.Code)
                .Cascade(CascadeMode.Stop)
                .NotNull().NotEmpty()
                .MustAsync(BeARegisteredRecoverAccountCode).WithMessage("The code has expired. Please request a new one to continue");

            RuleFor(u => u.Password)
                .Cascade(CascadeMode.Stop)
                .MustAsync(BeAStrongPassword)
                .WithMessage("Password is weak. Ensure at least one lower and upper case letter, one special character, one digit and minimum 8 characters length")
                .MustAsync(CheckBlackListPassword)
                .WithMessage("This password can not be used.")
                .When(x => !string.IsNullOrWhiteSpace(x.Password));

            RuleFor(x => x.CaptchaCode)
               .Cascade(CascadeMode.Stop)
               .Cascade(CascadeMode.Stop)
               .MustAsync(MustMatchCaptcha).WithMessage("Captcha doesn't match")
               .When(x => !string.IsNullOrWhiteSpace(x.CaptchaCode));
        }

        private async Task<bool> MustMatchCaptcha(string captchaCode, CancellationToken cancellationToken)
        {
            var configurationName = (await GetCaptchaConfig())?.Provider ?? "";
            var verifyCaptchaQueryResponse = await _captchaService.VerifyCaptchaAsync(new VerifyCaptchaRequest { VerificationCode = captchaCode, ConfigurationName = configurationName });

            return verifyCaptchaQueryResponse.Verified;
        }

        private async Task<bool> BeARegisteredRecoverAccountCode(string accountActivationCode, CancellationToken cancellationToken)
        {
            return await _cacheClient.KeyExistsAsync(accountActivationCode);
        }

        private async Task<CaptchaConfiguration> GetCaptchaConfig()
        {
            var captchaConfiguration = _dbContextProvider.GetCollection<CaptchaConfiguration>("CaptchaConfigurations");
            var configuration = await (await captchaConfiguration.FindAsync(Builders<CaptchaConfiguration>.Filter.Eq(mc => mc.IsEnable, true))).FirstOrDefaultAsync();
            return configuration;
        }
    }
}
