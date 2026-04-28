using FluentValidation;
using Iam.DomainService.Users;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DomainService.People
{

    public class TransferOwnershipRequestValidator : AbstractValidator<TransferOwnershipRequest>
    {
        private readonly IUserRepository _userRepository;

        public TransferOwnershipRequestValidator(IUserRepository userRepository)
        {
            _userRepository = userRepository;

            RuleFor(x => x.TenantGroupId)
           .NotEmpty()
           .WithMessage("TenantGroupId is required.");

            RuleFor(x => x.TransferToUserEmail)
                .NotEmpty()
                .WithMessage("TransferToUserEmail is required.")
                .EmailAddress()
                .WithMessage("TransferToUserEmail must be a valid email address.")
                .MustAsync(ExistingUser).WithMessage("Must be an existing user");
        }

        private async Task<bool> ExistingUser(string email, CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetUserByEmailAsync(email);

            return user != null;
        }
    }
}
