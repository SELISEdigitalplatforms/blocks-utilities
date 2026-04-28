using Blocks.Genesis;
using Iam.DomainService.Shared.Dtos;
using Microsoft.Extensions.Options;
using Worker.Configuration;


namespace Worker.Consumers.Users
{
    public class UserStatusChangedConsumer : IConsumer<UserStatusChangedEvent>
    {
        private readonly IHttpService _httpService;
        private readonly VerioSystemSettings _verioSystemSettings;

        public UserStatusChangedConsumer(IHttpService httpService, IOptions<VerioSystemSettings> verioSystemSettings)
        {
            _httpService = httpService;
            _verioSystemSettings = verioSystemSettings.Value;
        }

        public async Task Consume(UserStatusChangedEvent context)
        {
            context.ApiKey = _verioSystemSettings.ApiKey;
            await _httpService.Put<UserStatusChangedEvent>(context, _verioSystemSettings.BaseUri);
        }
    }
}
