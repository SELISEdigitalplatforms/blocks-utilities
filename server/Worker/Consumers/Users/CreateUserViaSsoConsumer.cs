using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;

namespace Worker.Consumers
{
    public class CreateUserViaSsoConsumer : IConsumer<CreateUserViaSsoEvent>
    {
        private readonly IUserManagementMutationService _userManagementMutationService;

        public CreateUserViaSsoConsumer(IUserManagementMutationService userManagementMutationService)
        {
            _userManagementMutationService = userManagementMutationService;
        }
        public async Task Consume(CreateUserViaSsoEvent context)
        {
            await _userManagementMutationService.ExecuteUserMutationViaSsoCommandAsync(context);
        }
    }
}
