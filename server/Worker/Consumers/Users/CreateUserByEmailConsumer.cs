using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;

namespace Worker.Consumers
{
    public class CreateUserByEmailConsumer : IConsumer<CreateUserByEmailEvent>
    {
        private readonly IUserManagementMutationService _userManagementMutationService;

        public CreateUserByEmailConsumer(IUserManagementMutationService userManagementMutationService)
        {
            _userManagementMutationService = userManagementMutationService;
        }

        public async Task Consume(CreateUserByEmailEvent context)
        {
            await _userManagementMutationService.CreateUserByEmailAsync(context);
        }
    }
}
