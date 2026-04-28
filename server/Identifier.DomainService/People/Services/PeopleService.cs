using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using FluentValidation;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System.Text.Json;

namespace DomainService.People
{
    /// <summary>
    /// Service for managing people and project invitations
    /// </summary>
    public class PeopleService : IPeopleService
    {
        private static class CacheConstants
        {
            public const int InvitationCacheExpirationSeconds = 3600; // 1 hour
        }

        private static class ErrorCodes
        {
            public const string EmptyGroupId = "empty_group_id";
            public const string InvalidGroupId = "invalid_group_id";
            public const string UserNotFound = "user_not_found";
            public const string CodeExpired = "code_expire";
            public const string AlreadySignedUp = "already_signup";
            public const string InvitationNotFound = "invitation_not_found";
        }

        private readonly ILogger<PeopleService> _logger;
        private readonly IPeopleRepository _peopleRepository;
        private readonly IMessageClient _messageClient;
        private readonly IConfiguration _configuration;
        private readonly ICacheClient _cacheClient;
        private readonly IValidator<SignupRequest> _validator;
        private readonly IValidator<TransferOwnershipRequest> _transferOwnerShipValidator;
        private readonly IProjectRepository _projectRepository;
        private readonly IUserManagementMutationService _iamDriverService;
        private readonly ITenants _tenants;


        public PeopleService(
            ILogger<PeopleService> logger,
            IPeopleRepository peopleRepository,
            IMessageClient messageClient,
            IConfiguration configuration,
            ICacheClient cacheClient,
            IUserManagementMutationService iamDriverService,
            IValidator<SignupRequest> validator,
            IValidator<TransferOwnershipRequest> transferOwnerShipValidator,
            IProjectRepository projectRepository,
            ITenants tenants)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _peopleRepository = peopleRepository ?? throw new ArgumentNullException(nameof(peopleRepository));
            _messageClient = messageClient ?? throw new ArgumentNullException(nameof(messageClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _cacheClient = cacheClient ?? throw new ArgumentNullException(nameof(cacheClient));
            _iamDriverService = iamDriverService ?? throw new ArgumentNullException(nameof(iamDriverService));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _transferOwnerShipValidator = transferOwnerShipValidator;
            _projectRepository = projectRepository;
            _tenants = tenants;
        }

        /// <summary>
        /// Retrieves people associated with a project group
        /// </summary>
        public async Task<GetPeoplesResponse> GetPeoplesAsync(GetPeoplesRequest request)
        {
            _logger.LogInformation("GetPeoplesAsync started for GroupId: {GroupId}", request?.ProjectGroupId);

            if (request == null || string.IsNullOrWhiteSpace(request.ProjectGroupId))
            {
                _logger.LogWarning("GetPeoplesAsync called with empty or null ProjectGroupId");
                return new GetPeoplesResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { ErrorCodes.EmptyGroupId, "Project groupId is required" }
                    }
                };
            }

            var sharedEnvironments = await _projectRepository.GetProjectPeoplesAsync(request.ProjectGroupId);
            if (sharedEnvironments == null || sharedEnvironments.Count == 0)
            {
                _logger.LogError("No projects are shared with user");
                return new GetPeoplesResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                        {
                            { "no_projects", "No projects are shared with user" }
                        }
                };
            }

            try
            {
                var result = await _peopleRepository.GetPeoplesAsync(request);

                var peoples = result.peoples
                    .GroupBy(p => p.peopleDetails)
                    .Select(g => new GetPeoples
                    {
                        peopleDetails = g.Key,
                        SharedEnviroments = g.Select(p => new SharedEnviroment
                        {
                            ItemId = p.ItemId,
                            TenantId = p.TenantId,
                            IsInvitationSent = p.IsInvitationSent,
                            IsInvitationConfirmed = p.IsInvitationConfirmed,
                            IsCreator = p.IsCreator,
                            Enviroment = p.Enviroment
                        }).ToList()
                    })
                    .ToList();

                _logger.LogInformation("GetPeoplesAsync completed successfully. Total count: {TotalCount}", result.totalCount);

                return new GetPeoplesResponse
                {
                    IsSuccess = true,
                    Peoples = peoples,
                    IsOwner = result.isOwner,
                    TotalCount = result.totalCount,
                    PeoplesTotalCount = result.peoplesTotalCount
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while getting peoples for GroupId: {GroupId}", request.ProjectGroupId);
                throw;
            }
        }

        /// <summary>
        /// Invites people to projects within a group
        /// </summary>
        public async Task<InviteResponse> InvitePeoplesAsync(InviteRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.GroupId))
            {
                _logger.LogWarning("InvitePeoplesAsync called with invalid request");
                return new InviteResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { ErrorCodes.InvalidGroupId, "GroupId is required" }
                    }
                };
            }


            _logger.LogInformation("InvitePeoplesAsync started for GroupId: {GroupId}", request.GroupId);

            try
            {
                var tenants = await _projectRepository.GetProjectIdsByGroupId(request.GroupId);

                if (tenants == null || tenants.Count == 0)
                {
                    _logger.LogWarning("No tenants found for GroupId: {GroupId}", request.GroupId);
                    return new InviteResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { ErrorCodes.InvalidGroupId, "The given groupId is invalid" }
                        }
                    };
                }

                var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;

                if (!await _peopleRepository.IsOwner(userId, tenants))
                {
                    return new InviteResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { "own_project", "You are not allowed to share this projects" }
                        }
                    };
                }

                foreach (var (email, projectKeys) in request.Invitations)
                {
                    if (string.IsNullOrWhiteSpace(email) || email == BlocksContext.GetContext()?.UserName)
                    {
                        _logger.LogWarning("Skipping invitation with empty email");
                        continue;
                    }

                    var validProjectKeys = projectKeys?.Where(pk => tenants.Contains(pk)).ToList();

                    if (validProjectKeys == null || validProjectKeys.Count == 0)
                    {
                        _logger.LogWarning("No valid project keys found for email: {Email}", email);
                        continue;
                    }

                    await ProcessInvitationForEmail(email, validProjectKeys, tenants);
                }

                _logger.LogInformation("InvitePeoplesAsync completed successfully for GroupId: {GroupId}", request.GroupId);

                return new InviteResponse { IsSuccess = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while inviting peoples for GroupId: {GroupId}", request.GroupId);
                throw;
            }
        }

        /// <summary>
        /// Processes invitation for a single email address
        /// </summary>
        private async Task ProcessInvitationForEmail(string email, List<string> validProjectKeys, List<string> tenants)
        {
            var existingUsers = await _peopleRepository.GetUsersByEmailAsync(new List<string> { email });
            var user = existingUsers?.FirstOrDefault(u => u.Email == email);

            if (user == null)
            {
                _logger.LogInformation("User not found for email: {Email}. Creating new user.", email);
                await ProcessUserCreateAndInvitation(email, string.Join(";", validProjectKeys));
                return;
            }

            var projectPeoples = await ProcessInviteRequests(tenants, validProjectKeys, user);

            if (projectPeoples.Count > 0)
            {
                await _peopleRepository.InsertPeoplesAsync(projectPeoples);
                _logger.LogInformation("Inserted {Count} project people records for email: {Email}", projectPeoples.Count, email);
            }
        }

        /// <summary>
        /// Processes invitation requests for existing user
        /// </summary>
        private async Task<List<ProjectPeople>> ProcessInviteRequests(List<string> tenants, List<string> projectKeys, User user)
        {
            var projectPeoples = new List<ProjectPeople>();

            var existingPeople = await _peopleRepository.GetProjectPeoplesAsync(user.ItemId, tenants) ?? new List<ProjectPeople>();
            var existingProjectKeys = existingPeople.Select(p => p.TenantId).ToList();
            var newProjectKeys = projectKeys.Except(existingProjectKeys).ToList();

            if (newProjectKeys.Count == 0)
            {
                _logger.LogInformation("User {Email} already has access to all requested projects", user.Email);
                return projectPeoples;
            }

            var isFirstInvitation = existingPeople.Count == 0;

            foreach (var projectKey in newProjectKeys)
            {
                var projectPeople = new ProjectPeople
                {
                    ItemId = Guid.NewGuid().ToString(),
                    TenantId = projectKey,
                    Email = user.Email,
                    IsInvitationSent = true,
                    IsInvitationConfirmed = !isFirstInvitation,
                    UserId = user.ItemId,
                };
                projectPeoples.Add(projectPeople);
            }

            if (isFirstInvitation && newProjectKeys.Count > 0)
            {
                var project = await _peopleRepository.GetProjectByIdAsync(newProjectKeys[0]);
                if (project != null)
                {
                    var projectPeopleIds = projectPeoples.Select(x => x.ItemId).ToList();
                    await ProcessInvitation(user, projectPeopleIds, project, string.Empty);
                }
            }

            return projectPeoples;
        }

        /// <summary>
        /// Creates a new project people record
        /// </summary>
        private ProjectPeople CreateProjectPeople(User user, string projectKey, string email)
        {
            return new ProjectPeople
            {
                ItemId = Guid.NewGuid().ToString(),
                TenantId = projectKey,
                Email = email,
                IsInvitationSent = true,
                UserId = user.ItemId,
            };
        }

        /// <summary>
        /// Initiates user creation and invitation process
        /// </summary>
        public async Task<bool> ProcessUserCreateAndInvitation(string email, string projectKeys)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning("ProcessUserCreateAndInvitation called with empty email");
                return false;
            }

            try
            {
                var createUserCommand = new CreateUserByEmailEvent_Identifier
                {
                    Email = email,
                    EventQueue = IdentifierConstants.IdentifierQueueName,
                    EventType = IdentifierConstants.ProjectPeopleInvitationMailPurpose,
                    ProjectKey = projectKeys
                };

                await _messageClient.SendToConsumerAsync(
                    new ConsumerMessage<CreateUserByEmailEvent_Identifier>
                    {
                        ConsumerName = IdentifierConstants.IamQueue,
                        Payload = createUserCommand
                    }
                );

                _logger.LogInformation("User creation event sent for email: {Email}", email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending user creation event for email: {Email}", email);
                throw;
            }
        }

        /// <summary>
        /// Processes and sends invitation to user
        /// </summary>
        public async Task<bool> ProcessInvitation(User user, List<string> ids, Tenant project, string activationKey)
        {
            if (user == null)
            {
                _logger.LogWarning("ProcessInvitation called with null user");
                return false;
            }

            if (project == null)
            {
                _logger.LogWarning("ProcessInvitation called with null project");
                return false;
            }

            try
            {
                var invitationCode = await SendInvitationEmail(user, project);
                _logger.LogInformation("Invitation sent to {Email} with code: {Code}", user.Email, invitationCode);
                await CacheInvitation(ids, activationKey, invitationCode);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invitation for user: {Email}", user.Email);
                throw;
            }
        }

        /// <summary>
        /// Sends invitation email to user
        /// </summary>
        public async Task<string> SendInvitationEmail(User user, Tenant project)
        {
            var invitationCode = Guid.NewGuid().ToString("n");
            var invitationLink = GenerateInvitationLink(invitationCode);
            var sendMailCommand = CreateSendMailCommand(user, project, invitationLink);

            await _messageClient.SendToConsumerAsync(
                new ConsumerMessage<SendMail>
                {
                    ConsumerName = IdentifierConstants.MailQueue,
                    Payload = sendMailCommand
                }
            );

            _logger.LogInformation("Invitation email queued for {Email}", user.Email);
            return invitationCode;
        }

        /// <summary>
        /// Generates invitation link with code
        /// </summary>
        private string GenerateInvitationLink(string code)
        {
            var blocksAppHost = _configuration["BlocksAppHost"];
            if (string.IsNullOrWhiteSpace(blocksAppHost))
            {
                _logger.LogWarning("BlocksAppHost configuration is missing");
                blocksAppHost = "https://app.blocks.com"; // Fallback
            }

            return $"{blocksAppHost}/invitation?code={code}";
        }

        /// <summary>
        /// Creates send mail command
        /// </summary>
        private SendMail CreateSendMailCommand(User user, Tenant project, string invitationLink)
        {
            var displayName = string.IsNullOrWhiteSpace(user.FirstName)
                ? user.Email
                : $"{user.FirstName} {user.LastName}".Trim();

            var projectName = string.IsNullOrWhiteSpace(project.Name)
                ? project.ApplicationDomain
                : project.Name;

            return new SendMail
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = new Dictionary<string, string>
                {
                    { "ProjectInvitationLink", invitationLink },
                    { "DisplayName", displayName },
                    { "ProjectName", projectName }
                },
                Language = "en-US",
                Purpose = IdentifierConstants.ProjectPeopleInvitationMailPurpose,
                To = new[] { user.Email.ToLowerInvariant() }
            };
        }

        /// <summary>
        /// Caches invitation data
        /// </summary>
        private async Task CacheInvitation(List<string> ids, string activationKey, string invitationCode)
        {
            var cacheData = new CacheProjectPeopleInvitation
            {
                ProjectPeopleIds = string.Join(";", ids),
                UserActivationKey = activationKey
            };

            await _cacheClient.AddStringValueAsync(
                invitationCode,
                JsonSerializer.Serialize(cacheData),
                CacheConstants.InvitationCacheExpirationSeconds
            );

            _logger.LogInformation("Invitation cached with code: {Code}", invitationCode);
        }

        /// <summary>
        /// Removes user access from projects
        /// </summary>
        public async Task<BaseResponse> RemoveAccessFromProjectAsync(RemoveAccessRequest request)
        {
            if (request == null || request.ProjectKeys.Count == 0 || string.IsNullOrWhiteSpace(request.Email))
            {
                _logger.LogWarning("RemoveAccessFromProjectAsync called with invalid request");
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "invalid_request", "GroupId and Email are required" }
                    }
                };
            }

            var tenants = await _projectRepository.GetProjectIdsByGroupId(request.GroupId);

            if (tenants == null || tenants.Count == 0)
            {
                _logger.LogWarning("No tenants found for GroupId: {GroupId}", request.GroupId);
                return new InviteResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                        {
                            { ErrorCodes.InvalidGroupId, "The given groupId is invalid" }
                        }
                };
            }

            var userId = BlocksContext.GetContext()?.UserId ?? string.Empty;

            if (!await _peopleRepository.IsOwner(userId, tenants))
            {
                return new InviteResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                        {
                            { "own_project", "You are not allowed to share this projects" }
                        }
                };
            }

            try
            {
                request.ProjectKeys = request.ProjectKeys.Where(pk => tenants.Contains(pk)).ToList();
                if (request.ProjectKeys.Count == 0)
                {
                    _logger.LogWarning("No valid project keys found for GroupId: {GroupId}", request.GroupId);
                    return new BaseResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { ErrorCodes.InvalidGroupId, "The given groupId is invalid" }
                        }
                    };
                }
                var existingUsers = await _peopleRepository.GetUsersByEmailAsync(new List<string> { request.Email });
                var user = existingUsers?.FirstOrDefault(u => u.Email == request.Email);

                if (user == null)
                {
                    _logger.LogWarning("User not found with email: {Email}", request.Email);
                    return new BaseResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { ErrorCodes.UserNotFound, $"User with email {request.Email} is not found" }
                        }
                    };
                }

                var result = await _peopleRepository.RemovePeoplesAsync(request.Email, request.ProjectKeys);

                //await _messageClient.SendToConsumerAsync(
                //    new ConsumerMessage<UpdateResourceUsageCommand>
                //    {
                //        ConsumerName = IdentifierConstants.IdentifierQueueName,
                //        Payload = new UpdateResourceUsageCommand
                //        {
                //            Resource = "blocks-identifier-api::people::invite",
                //            TenantId = request.ProjectKey,
                //            Amount = -1
                //        }
                //    }
                //);

                _logger.LogInformation("Access removed for Email: {Email}, Result: {Result}", request.Email, result);

                return new RemoveAccessResponse { IsSuccess = result };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing access for Email: {Email}", request.Email);
                throw;
            }
        }

        /// <summary>
        /// Sends project invitation to newly created user
        /// </summary>
        public async Task<bool> SendProjectInvitationToNewUser(CreateUserByEmailPostEvent_Identifier @event)
        {
            if (@event == null || string.IsNullOrWhiteSpace(@event.UserId) || string.IsNullOrWhiteSpace(@event.ProjectKey))
            {
                _logger.LogWarning("SendProjectInvitationToNewUser called with invalid event");
                return false;
            }

            _logger.LogInformation("SendProjectInvitationToNewUser started for UserId: {UserId}", @event.UserId);

            try
            {
                var projectKeys = @event.ProjectKey.Split(';', StringSplitOptions.RemoveEmptyEntries);

                if (projectKeys.Length == 0)
                {
                    _logger.LogWarning("No valid project keys found in event");
                    return false;
                }

                var project = await _peopleRepository.GetProjectByIdAsync(projectKeys[0]);
                if (project == null)
                {
                    _logger.LogWarning("Project not found with ID: {ProjectId}", projectKeys[0]);
                    return false;
                }

                var user = await _peopleRepository.GetUserByIdAsync(@event.UserId);
                if (user == null)
                {
                    _logger.LogWarning("User not found with ID: {UserId}", @event.UserId);
                    return false;
                }

                var projectPeoples = projectKeys
                    .Select(projectKey => CreateProjectPeople(user, projectKey, user.Email))
                    .ToList();

                await _peopleRepository.InsertPeoplesAsync(projectPeoples);

                var projectPeopleIds = projectPeoples.Select(x => x.ItemId).ToList();
                var result = await ProcessInvitation(user, projectPeopleIds, project, @event.Key);

                _logger.LogInformation("Project invitation sent to new user: {Email}, Result: {Result}", user.Email, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending invitation to new user: {UserId}", @event.UserId);
                throw;
            }
        }

        /// <summary>
        /// Confirms user invitation
        /// </summary>
        public async Task<ConfirmInvitationResponse> ConfirmInvitationAsync(ConfirmInvitationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Code))
            {
                _logger.LogWarning("ConfirmInvitationAsync called with empty code");
                return new ConfirmInvitationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "invalid_code", "Invitation code is required" }
                    }
                };
            }

            _logger.LogInformation("ConfirmInvitationAsync started for code: {Code}", request.Code);

            try
            {
                var cachedValue = await _cacheClient.GetStringValueAsync(request.Code);

                if (string.IsNullOrWhiteSpace(cachedValue))
                {
                    _logger.LogWarning("Invitation code not found or expired: {Code}", request.Code);
                    return new ConfirmInvitationResponse
                    {
                        Errors = new Dictionary<string, string>
                        {
                            { ErrorCodes.CodeExpired, "The invitation code has expired or is invalid" }
                        }
                    };
                }

                var parsedData = JsonSerializer.Deserialize<CacheProjectPeopleInvitation>(cachedValue);

                if (parsedData == null || string.IsNullOrWhiteSpace(parsedData.ProjectPeopleIds))
                {
                    _logger.LogWarning("Invalid cached data for code: {Code}", request.Code);
                    return new ConfirmInvitationResponse
                    {
                        Errors = new Dictionary<string, string>
                        {
                            { "invalid_data", "Invalid invitation data" }
                        }
                    };
                }

                var ids = parsedData.ProjectPeopleIds.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                await _peopleRepository.UpdateProjectPeoples(ids);
                await _cacheClient.RemoveKeyAsync(request.Code);

                _logger.LogInformation("Invitation confirmed successfully for code: {Code}", request.Code);

                return new ConfirmInvitationResponse
                {
                    IsSuccess = true,
                    ActivationKey = parsedData.UserActivationKey ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error confirming invitation for code: {Code}", request.Code);
                throw;
            }
        }

        /// <summary>
        /// Resends invitation to user
        /// </summary>
        public async Task<BaseResponse> ResendInvitationAsync(ResendInvitationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.GroupId) || string.IsNullOrWhiteSpace(request.Email))
            {
                _logger.LogWarning("ResendInvitationAsync called with invalid request");
                return new BaseResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { "invalid_request", "GroupId and Email are required" }
                    }
                };
            }

            _logger.LogInformation("ResendInvitationAsync started for Email: {Email}, GroupId: {GroupId}",
                request.Email, request.GroupId);

            try
            {
                var tenants = await _projectRepository.GetProjectIdsByGroupId(request.GroupId);

                if (tenants == null || tenants.Count == 0)
                {
                    _logger.LogWarning("No tenants found for GroupId: {GroupId}", request.GroupId);
                    return new BaseResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { ErrorCodes.InvalidGroupId, "The given groupId is invalid" }
                        }
                    };
                }

                var bc = BlocksContext.GetContext();

                if (!await _peopleRepository.IsOwner(bc.UserId, tenants))
                {
                    return new InviteResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { "own_project", "You are not allowed to share this projects" }
                        }
                    };
                }

                var existingUsers = await _peopleRepository.GetUsersByEmailAsync(new List<string> { request.Email });
                var user = existingUsers?.FirstOrDefault(u => u.Email == request.Email);

                if (user == null)
                {
                    _logger.LogWarning("User not found with email: {Email}", request.Email);
                    return new BaseResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { ErrorCodes.UserNotFound, $"User with email {request.Email} is not found" }
                        }
                    };
                }

                var existingPeople = await _peopleRepository.GetProjectPeoplesAsync(user.ItemId, tenants);

                if (existingPeople == null || existingPeople.Count == 0)
                {
                    _logger.LogWarning("No invitations found for email: {Email}", request.Email);
                    return new BaseResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { ErrorCodes.InvitationNotFound, $"No invitation found for email {request.Email} with the given groupId" }
                        }
                    };
                }

                var projectPeopleIds = existingPeople.Select(p => p.ItemId).ToList();
                var projectId = existingPeople.First().TenantId;
                var project = await _peopleRepository.GetProjectByIdAsync(projectId);

                if (project == null)
                {
                    _logger.LogWarning("Project not found with ID: {ProjectId}", projectId);
                    return new BaseResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { "project_not_found", "Associated project not found" }
                        }
                    };
                }

                var result = await ProcessInvitation(user, projectPeopleIds, project, string.Empty);

                _logger.LogInformation("Invitation resent to {Email}, Result: {Result}", request.Email, result);

                return new ResendInvitationResponse { IsSuccess = result };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resending invitation to {Email}", request.Email);
                throw;
            }
        }

        /// <summary>
        /// Handles user signup
        /// </summary>
        public async Task<SignupResponse> SignupAsync(SignupRequest request)
        {
            try
            {
                var validationResult = await _validator.ValidateAsync(request);

                if (!validationResult.IsValid)
                {
                    return new SignupResponse
                    {
                        IsSuccess = false,
                        Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                    };
                }

                var existingUsers = await _peopleRepository.GetUsersByEmailAsync(new List<string> { request.Email });

                if (existingUsers != null && existingUsers.Count > 0 && existingUsers.All(u=>u.Active) && existingUsers.All(u=>u.IsVarified))
                {
                    _logger.LogWarning("User already exists with email: {Email}", request.Email);
                    return new SignupResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { ErrorCodes.AlreadySignedUp, $"{request.Email} is already registered" }
                        }
                    };
                }

                var createUserRequest = new CreateUserRequest
                {
                    Email = request.Email,
                    MailPurpose = string.Empty,
                    Memberships = new List<Iam.DomainService.Shared.Entities.OrganizationMembership>
                    {
                        new Iam.DomainService.Shared.Entities.OrganizationMembership
                        {
                            OrganizationId = "default",
                            Roles = new List<string> { "user" }
                        }
                    }
                };

                var result = await _iamDriverService.CreateUserAsync(createUserRequest);

                if (result == null)
                {
                    _logger.LogError("CreateUser returned null for email: {Email}", request.Email);
                    return new SignupResponse
                    {
                        IsSuccess = false,
                        Errors = new Dictionary<string, string>
                        {
                            { "creation_failed", "User creation failed" }
                        }
                    };
                }

                _logger.LogInformation("User signup completed for email: {Email}, Success: {Success}",
                    request.Email, result.IsSuccess);

                return new SignupResponse
                {
                    IsSuccess = result.IsSuccess,
                    Errors = result.Errors
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during signup for email: {Email}", request.Email);
                throw;
            }
        }

        public async Task<BaseResponse> TransferOwnershipAsync(TransferOwnershipRequest request)
        {
            var validationResult = await _transferOwnerShipValidator.ValidateAsync(request);

            if(!validationResult.IsValid)
                return new BaseResponse { Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };

            var tenantids = await _projectRepository.GetProjectIdsByGroupId(request.TenantGroupId);
            var bc = BlocksContext.GetContext();

            if(! await _peopleRepository.IsOwner(bc.UserId, tenantids) || bc.UserName == request.TransferToUserEmail)
            {
                return new BaseResponse { Errors = new Dictionary<string, string> { { "own_project", "You are not allowed to transfer ownership of this projects" } } };
            }

            var ownerProjectPeoples = await _peopleRepository.GetProjectPeoplesAsync(bc.UserId, tenantids);
            await _peopleRepository.UpdateProjectPeopleOwnerShipAsync([.. ownerProjectPeoples.Select(p => p.ItemId)], false);
            
            var user = (await _peopleRepository.GetUsersByEmailAsync([request.TransferToUserEmail])).FirstOrDefault();
            await _peopleRepository.UpdateProjectOwnerShipAsync([.. ownerProjectPeoples.Select(p => p.TenantId)], user.ItemId);

            List<string> projectPeopleIds = [];

            foreach (var tenantdId in tenantids)
            {
                var projectPeople = await _peopleRepository.GetProjectPeopleByTenantIdAndUserIdAsync(tenantdId, user.ItemId);

                if(projectPeople == null)
                {
                    projectPeople =  new ProjectPeople { ItemId = Guid.NewGuid().ToString(), UserId = user.ItemId, TenantId = tenantdId, Email = user?.Email?? "", IsCreator = true, LastUpdatedDate = DateTime.UtcNow, LastUpdatedBy = bc.UserId, IsInvitationConfirmed = true, IsInvitationSent = true};
                    await _peopleRepository.InsertPeoplesAsync([projectPeople]);
                }
                else
                {
                    projectPeopleIds.Add(projectPeople.ItemId);
                }

                await Task.Run(() => _tenants.UpdateTenantVersionAsync(new TenantCacheUpdateMessage
                {
                    Action = "upsert",
                    TenantId = tenantdId,
                    Tenant = _tenants.GetTenantByID(tenantdId)

                }));
            }

            if(projectPeopleIds.Count > 0)
            await _peopleRepository.UpdateProjectPeopleOwnerShipAsync(projectPeopleIds, true);

            return new BaseResponse { IsSuccess = true };
        }
    }
}