using Authentication.DomainService.OAuth.SocialServices;
using Blocks.Extension.DependencyInjection;
using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using Captcha.DomainService.Utilities;
using DomainService.Authentication;
using DomainService.OAuth;
using DomainService.OAuth.Services;
using DomainService.OAuth.SocialServices;
using DomainService.Services;
using DomainService.Shared;
using FluentValidation;
using Iam.DomainService.Accounts;
using Iam.DomainService.Activities;
using Iam.DomainService.Configurations;
using Iam.DomainService.Resources;
using Iam.DomainService.Services;
using Iam.DomainService.Users;
using Mfa.DomainService.Configuration;
using Mfa.DomainService.OTP.Services;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Mfa.DomainService.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace DomainService.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void RegisterAllServices(this IServiceCollection serviceCollection)
        {
            #region Authentication
            serviceCollection.AddSingleton<IAuthenticationDomainService, AuthenticationDomainService>();
            serviceCollection.AddSingleton<IAuthenticationRepository, AuthenticationRepository>();

            serviceCollection.AddSingleton<IOAuthTokenProvider, OAuthTokenProvider>();
            serviceCollection.AddSingleton<IOAuthJwtAccessTokenManager, OAuthJwtAccessTokenManager>();
            serviceCollection.AddSingleton<IJwtAccessTokenProvider, JwtAccessTokenProvider>();

            serviceCollection.AddSingleton<IAuthenticationService, AuthenticationService>();

            serviceCollection.AddSingleton<PasswordAuthenticationService>();
            serviceCollection.AddSingleton<MfaAuthorizationService>();
            serviceCollection.AddSingleton<RefreshTokenAuthenticationService>();
            serviceCollection.AddSingleton<SocialAuthorizationService>();
            serviceCollection.AddSingleton<AuthorizeCodeService>();
            serviceCollection.AddSingleton<BYOSsoAuthorizationService>();
            serviceCollection.AddSingleton<BiometricAuthorizationService>();
            serviceCollection.AddSingleton<ClientCredentialAuthorizationService>();
            serviceCollection.AddSingleton<ClientUserCodeAuthorizationService>();
            serviceCollection.AddSingleton<SSOConsentAuthenticationService>();

            serviceCollection.AddSingleton<ICertificateProviderFactory, CertificateProviderFactory>();
            serviceCollection.AddSingleton<ISocialLogInServiceProvider, SocialLogInServiceProvider>();

            serviceCollection.AddSingleton<GoogleLogInService>();
            serviceCollection.AddSingleton<MicrosoftLogInService>();
            serviceCollection.AddSingleton<BYOSsoLogInService>();
            serviceCollection.AddSingleton<GithubLogInService>();
            serviceCollection.AddSingleton<LinkedinLogInService>();
            serviceCollection.AddSingleton<TwitterLogInService>();
            serviceCollection.AddSingleton<AppleLogInService>();
            serviceCollection.AddSingleton<FaceBookLogInService>();

            serviceCollection.AddSingleton<IIamConfigurationRepository, IamConfigurationRepository>();
            serviceCollection.AddTransient<IValidator<SaveSsoCredentialRequest>, SaveSsoCredentialRequestValidator>();

            #endregion

            #region IAM
            serviceCollection.AddSingleton<IUserManagementMutationService, UserManagementMutationService>();
            serviceCollection.AddSingleton<IUserRepository, UserRepository>();

            serviceCollection.AddSingleton<IIdentityAccessManagementService, IdentityAccessManagementService>();
            serviceCollection.AddSingleton<IIdentityAccessManagementRepository, IdentityAccessManagementRepository>();

            serviceCollection.AddSingleton<IResourceMutationService, ResourceMutationService>();
            serviceCollection.AddSingleton<IResourceRepository, ResourceRepository>();

            serviceCollection.AddSingleton<IUserManagementQueryService, UserManagementQueryService>();
            serviceCollection.AddSingleton<IResourceQueryService, ResourceQueryService>();

            serviceCollection.AddSingleton<IUserActivityRepository, UserActivityRepository>();
            serviceCollection.AddSingleton<IUserActivityService, UserActivityService>();
            serviceCollection.AddSingleton<IAccountService, AccountService>();
            serviceCollection.AddSingleton<IIamConfigurationRepository, IamConfigurationRepository>();

            //Validators
            serviceCollection.AddSingleton<IValidator<BaseAccountRequest>, BaseAccountValidator>();
            serviceCollection.AddSingleton<IValidator<ChangePasswordRequest>, ChangePasswordValidator>();
            serviceCollection.AddSingleton<IValidator<CreateUserRequest>, CreateUserValidator>();
            serviceCollection.AddSingleton<IValidator<UpdateUserRequest>, UpdateUserValidator>();
            serviceCollection.AddSingleton<IValidator<CreatePermissionRequest>, CreatePermissionValidator>();
            serviceCollection.AddSingleton<IValidator<CreateRoleRequest>, RoleValidator>();
            serviceCollection.AddSingleton<IValidator<UpdatePermissionRequest>, UpdatePermissionValidator>();
            serviceCollection.AddSingleton<IValidator<RecoveryUserRequest>, RecoveryUserRequestValidator>();

            #endregion

            #region MFA
            serviceCollection.AddSingleton<IMfaManagementService, MfaManagementService>();
            serviceCollection.AddSingleton<IOtpServiceFactory, OtpServiceFactory>();
            serviceCollection.AddSingleton<IMfaManagementRepository, MfaManagementRepository>();
            serviceCollection.AddSingleton<IMfaConfigurationService, MfaConfigurationService>();
            serviceCollection.AddSingleton<TotpService>();
            serviceCollection.AddSingleton<EmailOtpService>();
            serviceCollection.AddSingleton<ChangeControllerContext>();
            serviceCollection.AddHttpContextAccessor();

            serviceCollection.AddTransient<IValidator<VerifyOtpRequest>, VerifyOtpRequestValidator>();


            serviceCollection.RegisterBlocksMailService();

            #endregion

            #region Captcha

            // Verification handlers
            serviceCollection.AddSingleton<ReCaptchaVerificationService>();
            serviceCollection.AddSingleton<HCaptchaVerificationService>();
            serviceCollection.AddSingleton<BlocksCaptchaVerificationService>();

            // Register services
            serviceCollection.AddSingleton<ICaptchaService, CaptchaService>();
            serviceCollection.AddSingleton<ICaptchaConfigurationService, CaptchaConfigurationService>();
            serviceCollection.AddSingleton<ICaptchaConfigurationRepository, CaptchaConfigurationRepository>();
            serviceCollection.AddSingleton<ICaptchaGeneratorProvider, CaptchaGeneratorProvider>();
            serviceCollection.AddSingleton<IContextCaptchaIdGeneratorService, ContextCaptchaIdGeneratorService>();
            serviceCollection.AddSingleton<ICaptchaVerificationServiceProvider, CaptchaVerificationServiceProvider>();
            serviceCollection.AddSingleton<IHttpClientService, HttpClientService>();
            serviceCollection.AddSingleton<IRecaptchaConfigFactory, RecaptchaConfigFactory>();
            serviceCollection.AddSingleton<ICaptchaProcessor, CaptchaProcessor>();

            // Register validator
            serviceCollection.AddTransient<IValidator<CreateCaptchaRequest>, CreateCaptchaCommandValidator>();
            serviceCollection.AddTransient<IValidator<SubmitCaptchaRequest>, SubmitCaptchaCommandValidator>();

            #endregion
        }
    }
}