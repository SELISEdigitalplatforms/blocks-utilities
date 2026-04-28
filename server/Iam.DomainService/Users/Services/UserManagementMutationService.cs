using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Dtos;
using Iam.DomainService.Utilities;
using FluentValidation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Iam.DomainService.Shared.Entities;
using System.Security.Cryptography.Xml;

namespace Iam.DomainService.Users
{
    public class UserManagementMutationService : IUserManagementMutationService
    {
        private readonly ILogger<UserManagementMutationService> _logger;
        private readonly IValidator<CreateUserRequest> _createValidator;
        private readonly IValidator<UpdateUserRequest> _updateValidator;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly IUserRepository _userRepository;
        private readonly IMessageClient _messageClient;
        private readonly ICacheClient _cacheClient;
        private BlocksContext _blocksContext;

        public UserManagementMutationService(
            ILogger<UserManagementMutationService> logger,
            IValidator<CreateUserRequest> createValidator,
            IValidator<UpdateUserRequest> updateValidator,
            IIdentityAccessManagementService identityAccessManagementService,
            IUserRepository userRepository,
            IMessageClient messageClient,
            ICacheClient cacheClient
        )
        {
            _logger = logger;
            _createValidator = createValidator;
            _updateValidator = updateValidator;
            _identityAccessManagementService = identityAccessManagementService;
            _userRepository = userRepository;
            _messageClient = messageClient;
            _cacheClient = cacheClient;
        }

        public async Task<BaseMutationResponse> CreateUserAsync(CreateUserRequest command)
        {
            _logger.LogInformation("User creation start");

            var validationResult = await _createValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                _logger.LogInformation("User creation end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            _blocksContext = BlocksContext.GetContext();

            var itemId = await ProcessAsync(command);
            await SendEvent(itemId, MutationEventType.Create);

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<UpdateResourceUsageCommand>
            {
                ConsumerName = Constants.IdentifierQueue,
                Payload = new UpdateResourceUsageCommand
                {
                    Resource = "blocks-idp-api::iam::create",
                    TenantId = _blocksContext.TenantId,
                    Amount = 1
                }
            });

            _logger.LogInformation("User creation end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        private async Task SendEvent(string itemId, MutationEventType mutationEventType)
        {
            _logger.LogInformation("User mutation event -- initiate");
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<UserMutationEvent>
                {
                    ConsumerName = Constants.IamQueue,
                    Payload = new UserMutationEvent
                    {
                        ItemId = itemId,
                        Action = mutationEventType
                    }
                }
            );
            _logger.LogInformation("User mutation event -- sent");
        }

        public async Task<string> ProcessAsync(CreateUserRequest command)
        {
            var user = await _userRepository.GetUserByEmailAsync(command.Email);

            if(user is not null)
            {
                user.OrganizationIds = [.. user.OrganizationIds, command.OrganizationId];
                user.Memberships = [new OrganizationMembership { OrganizationId = command.OrganizationId, Roles = ["user"] }] ;
                await _userRepository.UpdateUserAsync(user);
                return user.ItemId;
            }

           user = await CreateNewUser(command);
           return user.ItemId;
        }

        private async Task<User> CreateNewUser(CreateUserRequest command)
        {
            var user = MapUser(command);
            await _userRepository.CreateUserAsync(user);
            return user;
        }

        public User MapUser(CreateUserRequest command)
        {
            var id = Guid.NewGuid().ToString();
            var user = new User
            {
                ItemId = id,
                CreatedDate = DateTime.Now,
                CreatedBy = _blocksContext?.UserId ?? id,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = _blocksContext?.UserId ?? id,
                Email = string.IsNullOrWhiteSpace(command.Email) ? string.Empty : command.Email.ToLower(),
                UserName = (string.IsNullOrWhiteSpace(command.UserName) ? command.Email : command.UserName).ToLower(),
                Password = string.IsNullOrWhiteSpace(command.Password) ? string.Empty : _identityAccessManagementService.HashPassword(command.Password),
                PasswordSetTime = string.IsNullOrWhiteSpace(command.Password) ? DateTime.MinValue : DateTime.Now,
                PhoneNumber = command.PhoneNumber ?? string.Empty,
                Language = command.Language ?? "en-US",
                Salutation = command.Salutation ?? string.Empty,
                FirstName = command.FirstName ?? string.Empty,
                LastName = command.LastName ?? string.Empty,
                Platform = command.Platform,
                OrganizationIds = [command.OrganizationId ?? "default"],
                Memberships = command.Memberships.Count == 0? [new OrganizationMembership { OrganizationId = command.OrganizationId ?? "default" , Roles = ["user"]}]: command.Memberships,
                UserCreationType = command.UserCreationType,
                UserPassType = command.UserPassType,
                Tags = command.Tags ?? [],
                VarifiedType = command.VarifiedType,
                ProfileImageUrl = command.ProfileImageUrl,
                ProfileImageId = command.ProfileImageId,
                AllowedLogInType = command.AllowedLogInType,
                MfaEnabled = command.MfaEnabled,
                UserMfaType = command.UserMfaType,
                MailPurpose = string.IsNullOrWhiteSpace(command.MailPurpose) ? "AccountActivation" : command.MailPurpose,
            };

            return user;
        }

        public async Task<BaseMutationResponse> UpdateUserAsync(UpdateUserRequest command)
        {
            _logger.LogInformation("User update start");

            var validationResult = _updateValidator.Validate(command);

            if (!validationResult.IsValid)
            {
                _logger.LogInformation("User update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var user = await _userRepository.GetUserByIdAsync(command.ItemId);
            if (user == null)
            {
                _logger.LogInformation("User update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "Not found" }
                    }
                };
            }

            _blocksContext = BlocksContext.GetContext();

            user.Salutation = command.Salutation ?? string.Empty;
            user.FirstName = command.FirstName ?? string.Empty;
            user.LastName = command.LastName ?? string.Empty;
            user.PhoneNumber = command.PhoneNumber ?? string.Empty;
            user.LastUpdatedDate = DateTime.Now;
            user.LastUpdatedBy = _blocksContext?.UserId ?? user.ItemId;
            user.Tags = command.Tags ?? user.Tags;
            user.ProfileImageId = command.ProfileImageId ?? string.Empty;
            user.ProfileImageUrl = command.ProfileImageUrl ?? string.Empty;
            user.MfaEnabled = command.MfaEnabled;
            
            user.OrganizationIds = [.. command.Memberships.Select(m => m.OrganizationId).Distinct()];
            user.Memberships = command.Memberships;

            if (command.MfaEnabled)
            {
                user.UserMfaType = command.UserMfaType;
            }


            var result = await _userRepository.UpdateUserAsync(user);

            if (!result)
            {
                _logger.LogInformation("User update end -- Error");
                return new BaseMutationResponse();
            }

            _logger.LogInformation("User update end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = user.ItemId
            };
        }

        public async Task<BaseResponse> DeactivateUserAsync(DeactivateUserRequest request)
        {
            var user = await _userRepository.GetUserByIdAsync(request.UserId);
            if (user == null)
            {
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "user_not_found", $"No user found with id {request.UserId}" } } };
            }

            user.Active = false;
            user.LastUpdatedBy = BlocksContext.GetContext()?.UserId ?? request.UserId;
            user.LastUpdatedDate = DateTime.Now;

            await Task.WhenAll(
            _userRepository.UpdateUserAsync(user),
            _messageClient.SendToConsumerAsync(new ConsumerMessage<UserStatusChangedEvent>
            {
                ConsumerName = Constants.IamQueue,
                Payload = new UserStatusChangedEvent
                {
                    UserId = request.UserId,
                    IsActive = false
                }
            }));

            return new BaseResponse { IsSuccess = true };
        }

        public async Task UpdateUserByLoginInfoAsync(RefreshTokenEvent refreshTokenConsumer)
        {
            _logger.LogInformation("User Mutation event -- initiate to update login info");

            var user = await _userRepository.GetUserByIdAsync(refreshTokenConsumer.UserId);

            if (user == null)
            {
                _logger.LogError("User not found by this user id: {Id}", refreshTokenConsumer.UserId);
                return;
            }

            if (user.LogInCount == 0)
            {
                user.FirstLoggedInTime = DateTime.Now;
            }

            user.LogInCount += 1;
            user.LastLoggedInTime = DateTime.Now;
            user.LastLoggedInDeviceInfo = JsonSerializer.Serialize(refreshTokenConsumer.DeviceInformation);

            await _userRepository.UpdateUserAsync(user);

            _logger.LogInformation("User Mutation event -- end of the update login info");
        }

        public async Task ExecuteUserMutationCommandAsync(UserMutationEvent command)
        {
            _logger.LogInformation("User Mutation event -- initiate");

            var user = await _userRepository.GetUserByIdAsync(command.ItemId);

            await SendActivationAsync(user);
            await SaveUserTimelineAsync(user);
        }

        private async Task<bool> SendActivationAsync(User user)
        {
            _logger.LogInformation("Send Activation for {Id}", user.ItemId);
            var config = await _userRepository.GetIamConfigurationAsync();
            var key = Guid.NewGuid().ToString("n");
            var accountActivationUri = string.Format("{0}?code={1}&lang={2}", config.AccountActivationUrl, key, user.Language);

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.ActivationUrlLifetimeInMinutes * 60);

            var emailPurpose = string.IsNullOrWhiteSpace(user.MailPurpose) ? "AccountActivation" : user.MailPurpose;
            var result = await _identityAccessManagementService.SendActivationToEmailAsync(user, accountActivationUri, emailPurpose, string.Empty);

            await _userRepository.InsertUserKeyMapAsync(new UserKeyMap
            {
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.Now,
                ExpireDate = DateTime.Now.AddMinutes(config.ActivationUrlLifetimeInMinutes),
                Value = accountActivationUri,
                MailPurpose = emailPurpose
            });

            _logger.LogInformation("Send Activation for {Id} is {Send}", user.ItemId, result ? "sent" : "not sent");
            return result;
        }

        private async Task<bool> SaveUserTimelineAsync(User user)
        {
            var blocksContext = BlocksContext.GetContext();
            var timeline = new UserTimeline
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedBy = blocksContext.UserId,
                CreatedDate = DateTime.Now,
                CurrentData = user,
                Event = "USER_CREATED"
            };

            await _userRepository.InsertUserTimelineAsync(timeline);
            return true;
        }

        public async Task<BaseMutationResponse> SaveRolesAndPermissionsAsync(SaveRolesAndPermissionsRequest command)
        {
            _logger.LogInformation("SaveRolesAndPermissions start");

            var user = await _userRepository.GetUserByIdAsync(command.UserId);
            if (user == null)
            {
                _logger.LogInformation("User update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "Not found" }
                    }
                };
            }

            user.Memberships = command.Memberships;
            user.OrganizationIds = [.. command.Memberships.Select(m => m.OrganizationId).Distinct()];
            var result = await _userRepository.UpdateUserAsync(user);

            if (!result)
            {
                _logger.LogInformation("SaveRolesAndPermissions end -- Error");
                return new BaseMutationResponse();
            }

            await SendEvent(user.ItemId, MutationEventType.Update);

            _logger.LogInformation("SaveRolesAndPermissions end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = user.ItemId
            };
        }

        public async Task<bool> CreateUserByEmailAsync(CreateUserByEmailEvent @event)
        {
            _logger.LogInformation("User creation start from CreateUserByEmail");

            var command = new CreateUserRequest
            {
                Email = @event.Email,
                UserCreationType = UserCreationType.Service,
                MailPurpose = @event.EventType,
                Memberships = [new OrganizationMembership {OrganizationId = "default", Permissions = [], Roles = ["user"] }]
            };

            _blocksContext = BlocksContext.GetContext();

            var validationResult = await _createValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                _logger.LogInformation("User creation end -- Validation Error -- CreateUserByEmail");
                return false;
            }

            var itemId = await ProcessAsync(command);

            await ProcessCreateUserByEmailAfterActionAsync(@event, itemId);

            _logger.LogInformation("User creation end -- Success -- CreateUserByEmail");
            return true;
        }

        public async Task<bool> ProcessCreateUserByEmailAfterActionAsync(CreateUserByEmailEvent @event, string userId)
        {
            var user = await _userRepository.GetUserByIdAsync(userId);

            var key = await CreateUserByEmailActivationProcessAsync(user, @event.EventType);

            await SaveUserTimelineAsync(user);

            await _identityAccessManagementService.SendToQueueAsync(@event.EventQueue, new CreateUserByEmailPostEvent
            {
                Key = key,
                UserId = userId,
                EventType = @event.EventType,
                ProjectKey = @event.ProjectKey,
            });

            return true;
        }

        public async Task<string> CreateUserByEmailActivationProcessAsync(User user, string eventType)
        {
            var config = await _userRepository.GetIamConfigurationAsync();

            var key = Guid.NewGuid().ToString("n");

            await _cacheClient.AddStringValueAsync(key, user.ItemId, config.ActivationUrlLifetimeInMinutes * 60);

            await _userRepository.InsertUserKeyMapAsync(new UserKeyMap
            {
                Key = key,
                UserId = user.ItemId,
                IssueDate = DateTime.Now,
                ExpireDate = DateTime.Now.AddMinutes(config.ActivationUrlLifetimeInMinutes),
                MailPurpose = eventType
            });

            return key;
        }

        public async Task<BaseMutationResponse> CreateUserViaSsoAsync(CreateUserViaSsoRequest command)
        {
            _logger.LogInformation("User creation start");

            _blocksContext = BlocksContext.GetContext();

            var itemId = await ProcessSsoUserAsync(command);

            _logger.LogInformation("User mutation event -- initiate");
            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<CreateUserViaSsoEvent>
                {
                    ConsumerName = Constants.IamQueue,
                    Payload = new CreateUserViaSsoEvent
                    {
                        ItemId = itemId,
                        Action = MutationEventType.Create,
                        MailPurpose = command.MailPurpose,
                        SendWelcomeMail = command.SendWelcomeMail,
                        ProjectKey = command.ProjectKey
                    }
                }
            );

            _logger.LogInformation("User creation end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        public async Task<string> ProcessSsoUserAsync(CreateUserViaSsoRequest command)
        {
            var id = Guid.NewGuid().ToString();
            var user = new User
            {
                ItemId = id,
                CreatedDate = DateTime.Now,
                CreatedBy = _blocksContext?.UserId ?? id,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = _blocksContext?.UserId ?? id,
                Email = string.IsNullOrWhiteSpace(command.Email) ? string.Empty : command.Email.ToLower(),
                UserName = string.IsNullOrWhiteSpace(command.Email) ? string.Empty : command.Email.ToLower(),
                Password = _identityAccessManagementService.HashPassword(Guid.NewGuid().ToString()),
                PasswordSetTime = DateTime.Now,
                PhoneNumber = command.PhoneNumber ?? string.Empty,
                Language = command.Language ?? "en-US",
                Salutation = command.Salutation ?? string.Empty,
                FirstName = command.FirstName ?? string.Empty,
                LastName = command.LastName ?? string.Empty,
                Platform = command.Platform,
                OrganizationIds = [BlocksContext.GetContext()?.OrganizationId ?? "default"],
                Memberships = command.Memberships,
                UserCreationType = command.UserCreationType,
                UserPassType = UserPassType.None,
                Tags = [],
                VarifiedType = UserVarifiedType.None,
                ProfileImageUrl = command.ProfileImageUrl,
                ProfileImageId = command.ProfileImageId,
                AllowedLogInType = command.AllowedLogInType,
                MailPurpose = command.MailPurpose,
                Active = command.Active,
                IsVarified = command.IsVarified,
                ExternalUserId = command.ExternalUserId,
                Department = command.DepartMent,
                EmployeeId = command.EmployeeId
            };
            await _userRepository.CreateUserAsync(user);

            return user.ItemId;
        }

        public async Task ExecuteUserMutationViaSsoCommandAsync(CreateUserViaSsoEvent command)
        {
            _logger.LogInformation("User Mutation event -- initiate");

            var user = await _userRepository.GetUserByIdAsync(command.ItemId);
            if (command.SendWelcomeMail)
            {
                await SendPostEventAsync(user, command.MailPurpose, command.ProjectKey);
            }
            await SaveUserTimelineAsync(user);
        }

        private async Task<bool> SendPostEventAsync(User user, string mailPurpose, string projectKey)
        {
            return await _identityAccessManagementService.SendAccountActivationEmailAsync(user, mailPurpose, projectKey);
        }

    }
}

