using Iam.DomainService.Enums;
using Iam.DomainService.Services;
using FluentValidation;

namespace Iam.DomainService.Resources
{
    public abstract class BasePermissionValidator<T> : AbstractValidator<T> where T : PermissionRequestBase
    {
        private readonly IResourceRepository _resourceRepository;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;

        protected BasePermissionValidator(IResourceRepository resourceRepository, IIdentityAccessManagementService identityAccessManagementService)
        {
            _resourceRepository = resourceRepository;
            _identityAccessManagementService = identityAccessManagementService;

            RuleFor(u => u.Name)
                .NotEmpty()
                .NotNull()
                .MaximumLength(150)
                .WithMessage("Maximum character limit 150 exceeded");

            RuleFor(u => u.Type)
                .NotEmpty()
                .NotNull()
                .IsInEnum();

            RuleFor(u => u.Resource)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .NotNull()
                .Must(resource => !HasSpaces(resource))
                .WithMessage("Resource cannot contain spaces.");

            RuleFor(u => u.ResourceGroup)
                .NotNull()
                .NotEmpty()
                .Must(rg => !rg.Contains(' ')).WithMessage("ResourceGroup must not contain spaces.");

            RuleFor(u => u.IsBuiltIn)
                .Must(IsRoot)
                .WithMessage("You are not allowed")
                .When(u => u.IsBuiltIn);

            RuleFor(u => u)
                .Must(IsValidEndpointStructure)
                .When(u => u.Type == ResourceType.Endpoint)
                .WithMessage("Endpoint resource must be in the format Service::Controller::Action");
        }

        protected async Task<bool> NotAnExistingResource(string resource, string itemId, CancellationToken cancellationToken)
        {
            var permission = await _resourceRepository.GetPermissionByResourceAsync(resource);
            return permission == null || permission.ItemId == itemId;
        }

        private static bool HasSpaces(string value)
        {
            return value?.Contains(" ") ?? false;
        }

        private static bool IsValidEndpointStructure(T request)
        {
            var parts = request.Resource?.Split("::").ToList();
            return parts?.Count == 3 && parts.TrueForAll(part => !string.IsNullOrWhiteSpace(part));
        }


        private bool IsRoot(bool isBuiltIn)
        {
            return _identityAccessManagementService.IsRoot();
        }
    }
}
