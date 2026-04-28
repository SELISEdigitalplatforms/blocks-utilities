using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using DomainService.People;
using FluentValidation;
using MongoDB.Driver;

public class SignupRequestValidator : AbstractValidator<SignupRequest>
{
    private ICaptchaService _captchaDriverService;
    private IDbContextProvider _dbContextProvider;
    private readonly IPeopleRepository _peopleRepository;
    private CaptchaConfiguration _captchaConfiguration;

    public SignupRequestValidator(ICaptchaService captchaDriverService,
                                  IDbContextProvider dbContextProvider,
                                  IPeopleRepository peopleRepository)
    {
        _captchaDriverService = captchaDriverService;
        _dbContextProvider = dbContextProvider;
        _peopleRepository = peopleRepository;

        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.")
            .MustAsync(IsAllowedForSignup).WithMessage("sign-up is disabled.");


        RuleFor(x => x.CaptchaCode)
               .Cascade(CascadeMode.Stop)
               .MustAsync(MustMatchCaptcha).WithMessage("Captcha verification is required. Please complete the captcha and try again.")
               .WhenAsync(async (x, _) => await IsCaptchaEnabledAsync());

    }

    private async Task<bool> MustMatchCaptcha(string captchaCode, CancellationToken cancellationToken)
    {
        var verifyCaptchaQueryResponse = await _captchaDriverService.VerifyCaptchaAsync( new VerifyCaptchaRequest { VerificationCode = captchaCode, ConfigurationName = _captchaConfiguration.Provider } );

        return verifyCaptchaQueryResponse.Verified;
    }

    private async Task<bool> IsCaptchaEnabledAsync()
    {
        var captchaConfiguration = _dbContextProvider.GetCollection<CaptchaConfiguration>("CaptchaConfigurations");
        _captchaConfiguration = await (await captchaConfiguration.FindAsync(Builders<CaptchaConfiguration>.Filter.Eq(mc => mc.IsEnable, true))).FirstOrDefaultAsync();
        return _captchaConfiguration != null;
    }

    private async Task<bool> IsAllowedForSignup(string email, CancellationToken cancellationToken)
    {
        var setting = await _peopleRepository.GetSignUpSettingAsync();
        return setting?.IsEmailPasswordSignUpEnabled ?? false;
    }
}
