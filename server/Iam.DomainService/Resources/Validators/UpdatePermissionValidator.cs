using Iam.DomainService.Services;
using FluentValidation;

namespace Iam.DomainService.Resources
{
    public class UpdatePermissionValidator : BasePermissionValidator<UpdatePermissionRequest>
    {
        public UpdatePermissionValidator(IResourceRepository resourceRepository, IIdentityAccessManagementService identityAccessManagementService)
            : base(resourceRepository, identityAccessManagementService)
        {
            RuleFor(u => u.Resource)
                .MustAsync((request, resource, cancellationToken) => NotAnExistingResource(resource, request.ItemId, cancellationToken))
                .WithMessage("Resource_Already_Exists");
        }
    }
}
