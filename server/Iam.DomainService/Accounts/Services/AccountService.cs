
using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Iam.DomainService.Users.RequestModel;
using System.Security.Cryptography.Xml;
using Iam.DomainService.Users.ResponseModel;
using Iam.DomainService.Shared.Entities;

namespace Iam.DomainService.Accounts
{
    public class AccountService : IAccountService
    {
        private readonly ILogger<AccountService> _logger;
        private readonly IIdentityAccessManagementRepository _repository;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly ICacheClient _cacheClient;
        private readonly IValidator<BaseAccountRequest> _accountValidator;
        private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;
        private readonly IValidator<RecoveryUserRequest> _recoverUserValidator;

        public AccountService(
            ILogger<AccountService> logger,
            IIdentityAccessManagementRepository repository,
            IIdentityAccessManagementService identityAccessManagementService,
            ICacheClient cacheClient,
            IValidator<BaseAccountRequest> accountValidator,
            IValidator<ChangePasswordRequest> changePasswordValidator,
            IValidator<RecoveryUserRequest> recoverUserValidator
        )
        {
            _logger = logger;
            _repository = repository;
            _identityAccessManagementService = identityAccessManagementService;
            _cacheClient = cacheClient;
            _accountValidator = accountValidator;
            _changePasswordValidator = changePasswordValidator;
            _recoverUserValidator = recoverUserValidator;
        }
        public async Task<BaseAccountResponse> ActivateAccountAsync(ActivateUserRequest activateUserRequest)
        {
            var validatorResult = await _accountValidator.ValidateAsync(activateUserRequest);
            if (!validatorResult.IsValid)
            {
                return new BaseAccountResponse
                {
                    Errors = validatorResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var result = await ProcessActivationAsync(activateUserRequest);

            return new BaseAccountResponse
            {
                IsSuccess = result
            };

        }

        public async Task<bool> ProcessActivationAsync(ActivateUserRequest activateUserRequest)
        {
            var userId = await _cacheClient.GetStringValueAsync(activateUserRequest.Code);

            var user = await _repository.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {Uid}", userId);
                return false;
            }

            user.Active = true;
            user.IsVarified = true;
            user.FirstName = activateUserRequest.FirstName;
            user.LastName = activateUserRequest.LastName;

            if (!string.IsNullOrWhiteSpace(activateUserRequest.Password))
            {
                user.Password = _identityAccessManagementService.HashPassword(activateUserRequest.Password);
            }

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                await _cacheClient.RemoveKeyAsync(activateUserRequest.Code);
                await _identityAccessManagementService.SendToQueueAsync(Constants.IamQueue, new AccountActivityEvent
                {
                    Code = activateUserRequest.Code,
                    UserId = userId,
                    MailPurpose = activateUserRequest.MailPurpose,
                    PreventPostEvent = activateUserRequest.PreventPostEvent,
                    Event = "Activate_Account"
                });
            }


            return result;
        }

        public async Task<BaseAccountResponse> RecoverAccountAsync(RecoveryUserRequest recoveryRequest)
        {
            var validationResult = await _recoverUserValidator.ValidateAsync(recoveryRequest);

            if (!validationResult.IsValid)
            {
                return new BaseAccountResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var user = await _repository.GetUserByEmailAsync(recoveryRequest.Email);

            if (user == null)
            {
                return new BaseAccountResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        {"Email", "Not_Allowed" }
                    }
                };
            }

            var result = await ProcessRecoverAccountAsync(user, recoveryRequest.MailPurpose, recoveryRequest.ProjectKey);

            return new BaseAccountResponse
            {
                IsSuccess = result,
            };
        }

        public async Task<bool> ProcessRecoverAccountAsync(User user, string emailPurpose, string projectKey)
        {
            var config = await _repository.GetIamConfigurationAsync();
            var key = Guid.NewGuid().ToString("n");
            var recoverAccountUrl = string.Format("{0}?code={1}&lang={2}", config.RecoverAccountUrl, key, user.Language);

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.RecoverAccountUrlLifetimeInMinutes * 60);

            emailPurpose = string.IsNullOrWhiteSpace(emailPurpose) ? "RecoverAccount" : emailPurpose;
            var result = await SendActivationToEmailAsync(user, recoverAccountUrl, emailPurpose, projectKey);

            await _repository.InsertUserKeyMapAsync(new UserKeyMap
            {
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.Now,
                ExpireDate = DateTime.Now.AddMinutes(config.ActivationUrlLifetimeInMinutes),
                Value = recoverAccountUrl,
                MailPurpose = emailPurpose
            });

            _logger.LogInformation("Send Activation for {UserId} is {Send}", user.ItemId, result ? "sent" : "not sent");
            return true;
        }

        public async Task<bool> SendActivationToEmailAsync(User user, string recoverAccountUrl, string emailPurpose, string projectKey)
        {
            _logger.LogInformation("Sending recovery for {Aid} by email: {MPurpose}", user.ItemId, emailPurpose);
            var sendMailCommand = new SendMail
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = new Dictionary<string, string>
                {
                    { "User.DisplayName" , $"{user.FirstName} {user.LastName}"},
                    { "EmailVerification.PageUrl" , recoverAccountUrl}
                },
                Language = user.Language ?? "en-US",
                Purpose = emailPurpose,
                To = new string[] { user.Email.ToLower() },
                ProjectKey = projectKey
            };

            return await _identityAccessManagementService.SendEmailAsync(sendMailCommand);
        }

        public async Task<BaseAccountResponse> ResetAccountPasswordAsync(ResetPasswordRequest resetPasswordRequest)
        {
            var validatorResult = await _accountValidator.ValidateAsync(resetPasswordRequest);
            if (!validatorResult.IsValid)
            {
                return new BaseAccountResponse
                {
                    Errors = validatorResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var result = await ProcessResetPasswordAsync(resetPasswordRequest);

            return new BaseAccountResponse
            {
                IsSuccess = result,
            };
        }

        public async Task<bool> ProcessResetPasswordAsync(ResetPasswordRequest resetPasswordRequest)
        {
            var userId = await _cacheClient.GetStringValueAsync(resetPasswordRequest.Code);

            var user = await _repository.GetUserByIdAsync(userId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {AccId}", userId);
                return false;
            }

            user.Password = _identityAccessManagementService.HashPassword(resetPasswordRequest.Password);

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                await _cacheClient.RemoveKeyAsync(resetPasswordRequest.Code);
                await _identityAccessManagementService.SendToQueueAsync(Constants.IamQueue, new AccountActivityEvent
                {
                    Code = resetPasswordRequest.Code,
                    UserId = userId,
                    Event = "Reset_Password",
                    PreventPostEvent = !resetPasswordRequest.LogoutFromAllDevices
                });
            }


            return result;
        }

        public async Task<BaseAccountResponse> ChangePasswordAsync(ChangePasswordRequest changePasswordRequest)
        {
            var validatorResult = await _changePasswordValidator.ValidateAsync(changePasswordRequest);
            if (!validatorResult.IsValid)
            {
                return new BaseAccountResponse
                {
                    Errors = validatorResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var result = await ProcessChangePasswordAsync(changePasswordRequest);

            return new BaseAccountResponse
            {
                IsSuccess = result,
            };
        }

        public async Task<bool> ProcessChangePasswordAsync(ChangePasswordRequest changePasswordRequest)
        {
            var bc = BlocksContext.GetContext();

            var user = await _repository.GetUserByIdAsync(bc?.UserId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {Id}", bc?.UserId);
                return false;
            }

            var oldhash = _identityAccessManagementService.HashPassword(changePasswordRequest.OldPassword);
            if (!oldhash.Equals(user.Password))
            {
                _logger.LogError("Password not matched");
                return false;
            }

            user.Password = _identityAccessManagementService.HashPassword(changePasswordRequest.NewPassword);

            var result = await _repository.UpdateUserAsync(user);

            if (result)
            {
                var config = await _repository.GetIamConfigurationAsync();
                await _identityAccessManagementService.SendToQueueAsync(Constants.IamQueue, new AccountActivityEvent
                {
                    UserId = bc?.UserId,
                    Event = "Change_Password",
                    PreventPostEvent = !config.LogoutOnPasswordChange
                });
            }
            return result;
        }

        public async Task<BaseAccountResponse> ResendActivationAsync(ResendActivationRequest resendActivationRequest)
        {
            if (string.IsNullOrWhiteSpace(resendActivationRequest.UserId))
            {
                return new BaseAccountResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "UserId", "UserId_Required" }
                    }
                };
            }

            var user = await _repository.GetUserByIdAsync(resendActivationRequest.UserId);
            if (user == null)
            {
                _logger.LogError("User not found for user id: {RuserId}", resendActivationRequest.UserId);
                return new BaseAccountResponse();
            }

            var result = await SendReActivationAsync(user, resendActivationRequest.ProjectKey);

            return new BaseAccountResponse
            {
                IsSuccess = result,
            };
        }

        public async Task<bool> SendReActivationAsync(User user, string projectKey)
        {
            var config = await _repository.GetIamConfigurationAsync();
            var key = Guid.NewGuid().ToString("n");
            var accountActivationUri = string.Format("{0}?code={1}&lang={2}", config.AccountActivationUrl, key, user.Language);

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.ActivationUrlLifetimeInMinutes * 60);

            var emailPurpose = string.IsNullOrWhiteSpace(user.MailPurpose) ? "AccountActivation" : user.MailPurpose;
            var result = await _identityAccessManagementService.SendActivationToEmailAsync(user, accountActivationUri, emailPurpose, projectKey);

            await _repository.InsertUserKeyMapAsync(new UserKeyMap
            {
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.Now,
                ExpireDate = DateTime.Now.AddMinutes(config.ActivationUrlLifetimeInMinutes),
                Value = accountActivationUri,
                MailPurpose = emailPurpose
            });

            _logger.LogInformation("Send ReActivation for {UsId} is {Send}", user.ItemId, result ? "sent" : "not sent");
            return result;
        }

        public async Task<ActivationCodeValidationResponse> ValidateAccountActivationCodeAsync(ValidateActivationCodeRequest validateActivationCodeRequest)
        {
            if (string.IsNullOrWhiteSpace(validateActivationCodeRequest.ActivationCode))
            {
                return new ActivationCodeValidationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ActivationCode", "ActivationCode_Required" }
                    }
                };
            }

            var isCodeExists = await _cacheClient.KeyExistsAsync(validateActivationCodeRequest.ActivationCode);

            if (isCodeExists)
                return new ActivationCodeValidationResponse { IsSuccess = true };

            var userId = await _repository.GetUserIdFromKeyMapByKeyAsync(validateActivationCodeRequest.ActivationCode);

            return new ActivationCodeValidationResponse
            {
                UserId = userId,
                IsSuccess = !string.IsNullOrWhiteSpace(userId),
                Errors = !string.IsNullOrWhiteSpace(userId) ? null : new Dictionary<string, string>
                {
                    { "ActivationCode", "Invalid_ActivationCode" }
                }
            };
        }

        public async Task<SaveSignUpSettingResponse> SaveSingUpSettingAsync(SaveSignUpSettingRequest request)
        {
            if(string.IsNullOrWhiteSpace(request.ItemId) && await _repository.SingnUpSettingAlreadyExist())
            {
                return new SaveSignUpSettingResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>{ { "sing_up_setting_exist", "SignUpSetting already exist" }}
                };
            }

            var settings = !string.IsNullOrWhiteSpace(request.ItemId) ?
                          await _repository.GetSingUpSettingByIdAsync(request.ItemId) :
                          new SignUpSetting { CreatedDate = DateTime.UtcNow, CreatedBy = BlocksContext.GetContext()?.UserId, ItemId = Guid.NewGuid().ToString() };

            settings.IsEmailPasswordSignUpEnabled = request.IsEmailPasswordSignUpEnabled;
            settings.IsSSoSignUpEnabled = request.IsSSoSignUpEnabled;
            settings.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            settings.LastUpdatedDate = DateTime.UtcNow;

            await _repository.SaveSingUpSettingAsync(settings);

            return new SaveSignUpSettingResponse { IsSuccess = true, ItemId = settings.ItemId };
        }

        public async Task<SignUpSetting> GetSignUpSettingAsync(GetSignUpSettingRequest request)
        {
            return  await _repository.GetSignUpSettingAsync(request.ItemId);
        }
    }
}
