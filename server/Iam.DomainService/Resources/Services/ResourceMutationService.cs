using Blocks.Genesis;
using FluentValidation;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Enums;
using Iam.DomainService.Resources.ResponseModel;
using Iam.DomainService.Services;
using Iam.DomainService.Shared.Entities;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using System.Security.AccessControl;

namespace Iam.DomainService.Resources
{
    public class ResourceMutationService : IResourceMutationService
    {
        private readonly ILogger<ResourceMutationService> _logger;
        private readonly IResourceRepository _resourceRepository;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly IValidator<CreatePermissionRequest> _permissionValidator;
        private readonly IValidator<UpdatePermissionRequest> _updatepPermissionValidator;
        private readonly IValidator<CreateRoleRequest> _roleValidator;

        public ResourceMutationService(
            ILogger<ResourceMutationService> logger,
            IResourceRepository resourceRepository,
            IIdentityAccessManagementService identityAccessManagementService,
            IValidator<CreatePermissionRequest> permissionValidator,
            IValidator<UpdatePermissionRequest> updatepPermissionValidator,
            IValidator<CreateRoleRequest> roleValidator
        )
        {
            _logger = logger;
            _resourceRepository = resourceRepository;
            _identityAccessManagementService = identityAccessManagementService;
            _permissionValidator = permissionValidator;
            _updatepPermissionValidator = updatepPermissionValidator;
            _roleValidator = roleValidator;
        }

        public async Task<BaseMutationResponse> CreatePermissionAsync(CreatePermissionRequest command)
        {
            _logger.LogInformation("Permission creation start");
            var validationResult = await _permissionValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                _logger.LogInformation("Permission creation end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }


            var itemId = await ProcessCreatePermissionRequestAsync(command);

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Create,
                    ItemId = itemId,
                    Entity = ResourceEntity.Permission
                }
            );

            _logger.LogInformation("Permission creation end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        public async Task<string> ProcessCreatePermissionRequestAsync(CreatePermissionRequest command)
        {
            var blocksContext = BlocksContext.GetContext();

            var permission = new Permission
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                Name = command.Name,
                Description = command.Description,
                Type = command.Type,
                Resource = command.Resource.ToLower(),
                Tags = command.Tags,
                IsBuiltIn = command.IsBuiltIn,
                ResourceGroup = command.ResourceGroup,
                PermissionSeverity = command.PermissionSeverity,
                DependentPermissions = command.DependentPermissions
                
            };
            await _resourceRepository.InsertPermissionAsync(permission);

            return permission.ItemId;

        }

        public async Task<BaseMutationResponse> CreateRoleAsync(CreateRoleRequest command)
        {
            _logger.LogInformation("Role creation start");

            var validationResult = await _roleValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var itemId = await ProcessRoleAsync(command);

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Create,
                    ItemId = itemId,
                    Entity = ResourceEntity.Role
                }
            );

            _logger.LogInformation("Role creation end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = itemId
            };
        }

        public async Task<string> ProcessRoleAsync(CreateRoleRequest command)
        {
            var blocksContext = BlocksContext.GetContext();
            var role = new Role
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                Name = command.Name,
                Description = command.Description,
                Slug = command.Slug.ToLower(),
            };

            await _resourceRepository.InsertRoleAsync(role);

            return role.ItemId;

        }

        public async Task<BaseMutationResponse> UpdatePermissionAsync(UpdatePermissionRequest command)
        {
            _logger.LogInformation("Permission update start");

            var permission = await _resourceRepository.GetPermissionByIdAsync(command.ItemId);
            if (permission == null)
            {
                _logger.LogInformation("Permission update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "Item_Not_Found" }
                    }
                };
            }

            var validationResult = await _updatepPermissionValidator.ValidateAsync(command);
            if (!validationResult.IsValid)
            {
                _logger.LogInformation("Permission update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var blocksContext = BlocksContext.GetContext();

            permission.Name = command.Name;
            permission.Description = command.Description;
            permission.Type = command.Type;
            permission.Resource = command.Resource.ToLower();
            permission.LastUpdatedDate = DateTime.Now;
            permission.LastUpdatedBy = blocksContext?.UserId;
            permission.Tags = command.Tags;
            permission.IsArchived = command.IsArchived;
            permission.IsBuiltIn = command.IsBuiltIn;
            permission.ResourceGroup = command.ResourceGroup;
            permission.DependentPermissions = command.DependentPermissions;
            permission.PermissionSeverity = command.PermissionSeverity;

            var result = await _resourceRepository.UpdatePermissionAsync(permission);

            if (!result)
            {
                _logger.LogInformation("Permission update end -- Error");
                return new BaseMutationResponse();
            }

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Update,
                    ItemId = permission.ItemId,
                    Entity = ResourceEntity.Permission
                }
            );

            _logger.LogInformation("Permission update end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = permission.ItemId
            };
        }

        public async Task<BaseMutationResponse> UpdateRoleAsync(UpdateRoleRequest command)
        {
            _logger.LogInformation("Role update start");
            var role = await _resourceRepository.GetRoleByIdAsync(command.ItemId);
            if (role == null)
            {
                _logger.LogInformation("Role update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "ItemId", "Item not found" }
                    }
                };
            }
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                _logger.LogInformation("Role update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "Name", "Should_Not_Be_Empty_Null" }
                    }
                };
            }

            if (command.Name.Count() > 150)
            {
                _logger.LogInformation("Role update end -- Validation Error");
                return new BaseMutationResponse
                {
                    Errors = new Dictionary<string, string>
                    {
                        { "Name", "Maximum_Character_Limit_100" }
                    }
                };
            }

            var blocksContext = BlocksContext.GetContext();

            role.Name = command.Name;
            role.Description = command.Description;
            role.LastUpdatedDate = DateTime.Now;
            role.LastUpdatedBy = blocksContext?.UserId;

            var result = await _resourceRepository.UpdateRoleAsync(role);

            if (!result)
            {
                _logger.LogInformation("Role update end -- Error");
                return new BaseMutationResponse();
            }

            await SendResourceMutationEventAsync(
                new ResourceMutationEvent
                {
                    Action = MutationEventType.Update,
                    ItemId = role.ItemId,
                    Entity = ResourceEntity.Role
                }
            );

            _logger.LogInformation("Role update end -- Success");
            return new BaseMutationResponse
            {
                IsSuccess = true,
                ItemId = role.ItemId
            };
        }

        public async Task SendResourceMutationEventAsync(ResourceMutationEvent resourceMutation)
        {
            _logger.LogInformation("Permission event -- initiate");
            await _identityAccessManagementService.SendToQueueAsync(
                Constants.IamQueue,
                resourceMutation
            );
            _logger.LogInformation("Permission event -- sent");
        }

        public async Task<SetRolesResponse> SetRolesAsync(SetRolesRequest command)
        {
            _logger.LogInformation("SetRole start");
            if (string.IsNullOrWhiteSpace(command.Slug))
            {
                _logger.LogError("Slug should not be empty or null");
                return new SetRolesResponse();
            }

            var isExist = await _resourceRepository.GetRoleBySlugAsync(command.Slug);

            if (isExist == null)
            {
                _logger.LogError("Role does not exist by this slug");
                return new SetRolesResponse();
            }

            if (command.AddPermissions.Any())
            {
                await _resourceRepository.UpdateRolePermissionByIdsAsync(command.Slug, command.AddPermissions);
            }

            if (command.RemovePermissions.Any())
            {
                await _resourceRepository.RemoveRolePermissionByIdsAsync(command.Slug, command.RemovePermissions);
            }

            await SendResourceSetToPermissionMutationEventAsync(
                new ResourceSetToPermissionMutationEvent
                {
                    Entity = ResourceEntity.Role,
                    AddPermissions = command.AddPermissions,
                    RemovePermissions = command.RemovePermissions,
                    Slug = command.Slug
                });

            _logger.LogInformation("SetRole end");

            return new SetRolesResponse
            {
                Success = true
            };

        }

        public async Task SendResourceSetToPermissionMutationEventAsync(ResourceSetToPermissionMutationEvent resourceMutation)
        {
            _logger.LogInformation("Permission event -- initiate");
            await _identityAccessManagementService.SendToQueueAsync(
                Constants.IamQueue,
                resourceMutation
            );
            _logger.LogInformation("Permission event -- sent");
        }

        public async Task ExecuteResourceMutationCommandAsync(ResourceMutationEvent command)
        {
            _logger.LogInformation("Resource Mutation event -- initiate");

            if (command == null)
            {
                _logger.LogWarning("Received null ResourceMutationEvent.");
                return;
            }

            switch (command.Entity)
            {
                case ResourceEntity.Permission:
                    await ProcessResourceMutationEventAsync(command);
                    break;
                case ResourceEntity.Role:
                    await ProcessRoleAsync(command);
                    break;
                default:
                    _logger.LogWarning("Unhandled MutationEventType: {MutationEventType}", command.Action);
                    break;
            }

            _logger.LogInformation("Resource Mutation event -- done");
        }

        private async Task<bool> ProcessResourceMutationEventAsync(ResourceMutationEvent context)
        {
            _logger.LogInformation("Processing permission timeline for ResourceMutationEvent.");

            var permission = await _resourceRepository.GetPermissionByIdAsync(context.ItemId);
            var blocksContext = BlocksContext.GetContext();
            var timeline = new ResourceTimeline<Permission>
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                CurrentData = permission,
                Event = context.Action.ToString().ToLower(),
                Entity = typeof(Permission).Name.ToLower(),
            };

            var result = await _resourceRepository.SaveResourceTimelineAsync(timeline);

            return result;
        }

        private async Task<bool> ProcessRoleAsync(ResourceMutationEvent context)
        {
            _logger.LogInformation("Processing role timeline for ResourceMutationEvent.");
            var role = await _resourceRepository.GetRoleByIdAsync(context.ItemId);
            var blocksContext = BlocksContext.GetContext();
            var timeline = new ResourceTimeline<Role>
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                CurrentData = role,
                Event = context.Action.ToString().ToLower(),
                Entity = typeof(Role).Name.ToLower(),
            };

            var result = await _resourceRepository.SaveResourceTimelineAsync(timeline);
            await _resourceRepository.UpdateRolesCountAsync(role.Slug);

            return result;
        }

        public async Task<bool> ProcessPermissionAsync(ResourceSetToPermissionMutationEvent command)
        {
            _logger.LogInformation("Processing permission timeline for ResourceMutationEvent.");
            var blocksContext = BlocksContext.GetContext();
            var timelines = new List<ResourceTimeline<Permission>>();
            foreach (var itemId in command.AddPermissions.Union(command.RemovePermissions))
            {
                timelines.Add(await ProcessTimelineAsync(blocksContext, itemId, command.Entity == ResourceEntity.Role ? "roleupdate" : "groupupdate"));
            }

            await _resourceRepository.SaveResourceTimelinesAsync(timelines);

            if (command.Entity == ResourceEntity.Role)
            {
                await _resourceRepository.UpdateRolesCountAsync(command.Slug);
            }
           

            return true;
        }

        private async Task<ResourceTimeline<Permission>> ProcessTimelineAsync(BlocksContext blocksContext, string itemId, string eventname)
        {
            var permission = await _resourceRepository.GetPermissionByIdAsync(itemId);

            return new ResourceTimeline<Permission>
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = blocksContext?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = blocksContext?.UserId,
                CurrentData = permission,
                Event = eventname,
                Entity = typeof(Permission).Name.ToLower(),
            };
        }

        public async Task<BaseResponse> SaveOrganizationAsync(SaveOrganizationRequest request)
        {
            var organization = await MapOrganizationAsync(request);
            await _resourceRepository.SaveOrganizationAsync(organization);
            return new BaseResponse { IsSuccess = true };
        }

        private async Task<Organization> MapOrganizationAsync(SaveOrganizationRequest request)
        {
            var userId = BlocksContext.GetContext()?.UserId;
            var organization = await _resourceRepository.GetOrganizationById(request.ItemId) ?? 
                           new Organization { ItemId = Guid.NewGuid().ToString(), CreatedBy = userId, CreatedDate = DateTime.UtcNow };

            organization.LastUpdatedDate = DateTime.UtcNow;
            organization.Name = request.Name;
            organization.LastUpdatedBy = userId;
            organization.IsEnable = request.IsEnable;

            return organization;
        }

        public async Task<GetOrganizationsResponse> GetOrganizationsAsync(GetOrganizationsRequest request)
        {
            return await _resourceRepository.GetOrganizationsAsync(request); 
        }

        public async Task<GetOrganizationResponse> GetOrganizationAsync(GetOrganizationRequest request)
        {
            var organization = await _resourceRepository.GetOrganizationById(request.ItemId);
            return new GetOrganizationResponse { IsSuccess = true, Organization = organization };
        }

        public async Task<BaseResponse> SaveganizationConfigAsync(SaveOrganizationConfigRequest request)
        {
            var organizationConfig = await MapOrganizationConfigAsync(request);
            await _resourceRepository.SaveOrganizationConfig(organizationConfig);

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<OrganizationConfig> GetOrganizationConfigAsync(GetOrganizationConfigRequest request)
        {
            return await _resourceRepository.GetOrganizationConfigAsync();
        }

        private async Task<OrganizationConfig> MapOrganizationConfigAsync(SaveOrganizationConfigRequest request)
        {
            var userId = BlocksContext.GetContext()?.UserId;
            var organizationConfig = await _resourceRepository.GetOrgConfigByIdAsync(request.ItemId) ?? new OrganizationConfig { ItemId = Guid.NewGuid().ToString(), CreatedDate = DateTime.UtcNow, CreatedBy = userId };
            
            organizationConfig.AllowCreationFromCloud = request.AllowCreationFromCloud;
            organizationConfig.AllowCreationFromConstruct = request.AllowCreationFromConstruct;
            organizationConfig.IsMultiOrgEnabled = request.IsMultiOrgEnabled;
            organizationConfig.LastUpdatedBy = userId;
            organizationConfig.LastUpdatedDate = DateTime.UtcNow;
            organizationConfig.Roles = request.Roles;

            return organizationConfig;
        }
    }
}
