using Blocks.Genesis;
using FluentValidation;
using Pipelines.Sockets.Unofficial.Arenas;

namespace Iam.DomainService.Users
{
    public class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
    {
        private readonly ITenants _tenants;

        public UpdateUserValidator(ITenants tenants)
        {
            _tenants = tenants;

            RuleFor(u => u.ItemId).NotEmpty().NotNull();
            RuleFor(u => u.FirstName).MaximumLength(150).WithMessage("Maximum character limit 150 exceeded").When(u => !string.IsNullOrWhiteSpace(u.FirstName));
            RuleFor(u => u.LastName).MaximumLength(150).WithMessage("Maximum character limit 150 exceeded").When(u => !string.IsNullOrWhiteSpace(u.LastName));
            RuleFor(u => u).Must(request => HavePermissionToChange(request, _tenants)).WithMessage("You don't have permission to update this user");
        }

        private static bool HavePermissionToChange(UpdateUserRequest request, ITenants tenants)
        {
            var context = BlocksContext.GetContext();
            var clientTenant = tenants.GetTenantByID(request?.ProjectKey ?? "");

            if ((clientTenant?.CreatedBy == context.UserId) || (context.UserId == request.ItemId))
            {
                return true;
            }

            return false;
        }
    }
}
