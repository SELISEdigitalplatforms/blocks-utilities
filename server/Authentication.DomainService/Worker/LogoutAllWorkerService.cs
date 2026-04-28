using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Services;
using DomainService.Utilities;
using Iam.DomainService.Dtos;

namespace DomainService.Worker
{
    public class LogoutAllWorkerService : IConsumer<LogoutAllEvent>
    {
        private readonly ICacheClient _cacheClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IAuthenticationDomainService _authenticationDomainService;

        public LogoutAllWorkerService(
            ICacheClient cacheClient,
            IAuthenticationRepository authenticationRepository,
            IAuthenticationDomainService authenticationDomainService)
        {
            _cacheClient = cacheClient;
            _authenticationRepository = authenticationRepository;
            _authenticationDomainService = authenticationDomainService;
        }
        public async Task Consume(LogoutAllEvent context)
        {
            var refreshTokens = (await _authenticationRepository.GetActiveSessionByUserIdAsync(context.UserId)).Select(x => x.RefreshToken).ToList();
            var cacheTask = refreshTokens.Select(async x => await _cacheClient.RemoveKeyAsync(x));
            await Task.WhenAll(cacheTask);

            await _authenticationRepository.UpdateSessionStatusForAllRefreshTokenAsync(refreshTokens);

            await ProcessTimeline(context.UserId);
        }

        public async Task<bool> ProcessTimeline(string userId)
        {
            var eventTimeline = new UserAuthenticationTimelineEvent
            {
                DeviceInformation = new DeviceInformation
                {
                    Device = "server"
                },
                Event = "revoke_access_by_logout_all",
                ActionBy = "call_api_to_logout_all",
                UserId = userId
            };

            await _authenticationDomainService.SendToQueueAsync(IdpConstants.AuthenticationQueue, eventTimeline);
            return true;
        }
    }
}
