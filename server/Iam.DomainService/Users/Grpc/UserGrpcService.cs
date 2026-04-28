using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Users
{
    public class UserGrpcService : Users.UsersBase
    {
        private readonly ILogger<UserGrpcService> _logger;
        private readonly IUserManagementMutationService _userManagementMutationService;

        public UserGrpcService(ILogger<UserGrpcService> logger, IUserManagementMutationService userManagementMutationService)
        {
            _logger = logger;
            _userManagementMutationService = userManagementMutationService;
        }

        public async override Task<SignupUserReply> SignupUser(SignupUserRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Start SignupUser");
            var command = new CreateUserRequest
            {
                Email = request.Email,
                MailPurpose = request.MailPurpose,
            };
            var result = await _userManagementMutationService.CreateUserAsync(command);

            var reply = new SignupUserReply
            {
                IsSuccess = result.IsSuccess,
                ItemId = result.ItemId ?? string.Empty,
            };

            if (result.Errors != null)
            {
                reply.Errors.Add(result.Errors);
            }

            return reply;
        }
    }
}
