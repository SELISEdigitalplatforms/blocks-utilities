using FluentValidation;

namespace Iam.DomainService.Resources
{
    public class RoleValidator : AbstractValidator<CreateRoleRequest>
    {
        private readonly IResourceRepository _resourceRepository;

        public RoleValidator(IResourceRepository resourceRepository)
        {
            _resourceRepository = resourceRepository;

            RuleFor(u => u.Name).NotEmpty().NotNull().MaximumLength(150).WithMessage("Maximum_Character_Limit_150");
            RuleFor(u => u.Slug)
               .Cascade(CascadeMode.Stop)
               .NotEmpty().NotNull()
               .WithMessage("Slug must not empty or null")
               .Must(resource => !HasSpaces(resource))
               .WithMessage("Resource name must not contain spaces")
               .MaximumLength(200).WithMessage("Resource name maximum character limit 200")
               .MustAsync(NotAnExistingResourceRole)
               .WithMessage("Role slug must be unique");

        }

        public async Task<bool> NotAnExistingResourceRole(string slug, CancellationToken cancellationToken)
        {
            var role = await _resourceRepository.GetRoleBySlugAsync(slug);
            return role == null;
        }

        private static bool HasSpaces(string value)
        {
            return value?.Contains(" ") ?? false;
        }
    }
}
