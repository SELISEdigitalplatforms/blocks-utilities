using Blocks.Genesis;
using Iam.DomainService.Users;

namespace Worker.Consumers
{
    public class CreateUserConsumer : IConsumer<CreateUserRequest>
    {
        private readonly IUserManagementMutationService _userManagementMutationService;
        public CreateUserConsumer(IUserManagementMutationService userManagementMutationService)
        {
            _userManagementMutationService = userManagementMutationService;
        }

        public async Task Consume(CreateUserRequest context)
        {
            await _userManagementMutationService.CreateUserAsync(context);
        }
    }
}
