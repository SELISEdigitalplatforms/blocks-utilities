using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Users;

namespace Worker.Consumers
{
    public class UpdateUserByLoginInfoConsumer : IConsumer<RefreshTokenEvent>
    {
        private readonly ILogger<UpdateUserByLoginInfoConsumer> _logger;
        private readonly IUserManagementMutationService _userManagementMutationService;

        public UpdateUserByLoginInfoConsumer
        (
            ILogger<UpdateUserByLoginInfoConsumer> logger,
            IUserManagementMutationService userManagementMutationService
        )
        {
            _logger = logger;
            _userManagementMutationService = userManagementMutationService;
        }
        public async Task Consume(RefreshTokenEvent context)
        {
            _logger.LogInformation("Start Consume for UpdateUserByLoginInfoAsync");
            await _userManagementMutationService.UpdateUserByLoginInfoAsync(context);
        }
    }

}
