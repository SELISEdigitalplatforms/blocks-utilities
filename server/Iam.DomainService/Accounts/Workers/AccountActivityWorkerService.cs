using Blocks.Genesis;
using Iam.DomainService.Dtos;
using Iam.DomainService.Entities;
using Iam.DomainService.Services;
using Iam.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Iam.DomainService.Accounts
{
    public class AccountActivityWorkerService : IConsumer<AccountActivityEvent>
    {
        private readonly ILogger<AccountActivityWorkerService> _logger;
        private readonly IIdentityAccessManagementRepository _repository;
        private readonly IIdentityAccessManagementService _identityAccessManagementService;
        private readonly ICacheClient _cacheClient;

        public AccountActivityWorkerService
        (
            ILogger<AccountActivityWorkerService> logger,
            IIdentityAccessManagementRepository repository,
            IIdentityAccessManagementService identityAccessManagementService,
            ICacheClient cacheClient
        )
        {
            _logger = logger;
            _repository = repository;
            _identityAccessManagementService = identityAccessManagementService;
            _cacheClient = cacheClient;
        }
        public async Task Consume(AccountActivityEvent context)
        {
            if(!string.IsNullOrWhiteSpace(context.Code))
            {
                var keys = (await _repository.GetActiveUserKeyMapAsync(context.UserId))?.Select(x => x.Key) ?? new List<string>();

                var cacheTask = keys.Select(async x => await _cacheClient.RemoveKeyAsync(x));
                await Task.WhenAll(cacheTask);

                await _repository.UpdateUserKeyMapActivationAsync(context.UserId);
            }

            var user = await _repository.GetUserByIdAsync(context.UserId);

            await SaveUserTimeline(user, context);
  
            _logger.LogInformation("Event type: {EvenType}", context.Event);
            if (!context.PreventPostEvent)
            {
                switch (context.Event)
                {
                    case "Activate_Account":
                        await HandlePostEventForActivation(user, context.MailPurpose, string.Empty);
                        break;
                    case "Reset_Password":
                        await HandlePostEventForResetPassword(context.UserId);
                        break;
                    default:
                        break;
                }

            }
        }

        public async Task<bool> SaveUserTimeline(User user, AccountActivityEvent context)
        {
            var blocksContext = BlocksContext.GetContext();

            var timeline = new UserTimeline
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedBy = string.IsNullOrWhiteSpace(blocksContext?.UserId) ? user.CreatedBy : blocksContext.UserId,
                CreatedDate = DateTime.Now,
                CurrentData = user,
                Event = context.Event
            };

            await _repository.InsertUserTimelineAsync(timeline);
            return true;
        }

        public async Task<bool> HandlePostEventForActivation(User user, string mailPurpose, string projectKey)
        {
            return await _identityAccessManagementService.SendAccountActivationEmailAsync(user, mailPurpose, projectKey);
        }

        public async Task<bool> HandlePostEventForResetPassword(string userId)
        {
            await _identityAccessManagementService.SendToQueueAsync(Constants.AuthenticationQueue, new LogoutAllEvent
            {
                UserId = userId
            });

            return true;

        }


    }
}
