using Iam.DomainService.Services;
using FluentValidation;

namespace Iam.DomainService.Resources
{
    public class CreatePermissionValidator : BasePermissionValidator<CreatePermissionRequest>
    {
        public CreatePermissionValidator(IResourceRepository resourceRepository, IIdentityAccessManagementService identityAccessManagementService)
            : base(resourceRepository, identityAccessManagementService)
        {
            RuleFor(u => u.Resource)
                .MustAsync((resource, cancellationToken) => NotAnExistingResource(resource, null, cancellationToken))
                .WithMessage("Resource_Already_Exists");
        }
    }
}
