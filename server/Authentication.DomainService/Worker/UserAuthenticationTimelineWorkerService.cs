using Blocks.Genesis;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Services;
using Microsoft.Extensions.Logging;

namespace DomainService.Worker
{
    public class UserAuthenticationTimelineWorkerService : IConsumer<UserAuthenticationTimelineEvent>
    {
        private readonly ILogger<UserAuthenticationTimelineWorkerService> _logger;
        private readonly IAuthenticationRepository _authenticationRepository;

        public UserAuthenticationTimelineWorkerService(ILogger<UserAuthenticationTimelineWorkerService> logger, IAuthenticationRepository authenticationRepository)
        {
            _logger = logger;
            _authenticationRepository = authenticationRepository;
        }
        public async Task Consume(UserAuthenticationTimelineEvent context)
        {
            _logger.LogInformation("UserAuthenticationTimelineWorkerService start");
            await ProcessUserTimelineEvent(context);
        }

        public async Task<bool> ProcessUserTimelineEvent(UserAuthenticationTimelineEvent context)
        {
            var userAuthenticationTimeline = new UserAuthenticationTimeline 
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                CreatedBy = context?.UserId,
                LastUpdatedDate = DateTime.Now,
                LastUpdatedBy = context?.UserId,
                DeviceInformation = context?.DeviceInformation,
                IpAddresses = context?.IpAddresses ?? string.Empty,
                Event = context?.Event ?? string.Empty,
                ActionBy = context?.ActionBy ?? string.Empty
            };

            return await _authenticationRepository.InsertUserAuthenticationTimelineAsync(userAuthenticationTimeline);
        }
    }
}
