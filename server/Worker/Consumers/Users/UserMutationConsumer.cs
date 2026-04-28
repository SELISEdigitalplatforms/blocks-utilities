using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;

namespace Worker.Consumers
{
    public class UserMutationConsumer : IConsumer<UserMutationEvent>
    {
        private readonly IUserManagementMutationService _userManagementMutationService;

        public UserMutationConsumer(IUserManagementMutationService userManagementMutationService)
        {
            _userManagementMutationService = userManagementMutationService;
        }
        public async Task Consume(UserMutationEvent context)
        {
            await _userManagementMutationService.ExecuteUserMutationCommandAsync(context);
        }
    }
}
