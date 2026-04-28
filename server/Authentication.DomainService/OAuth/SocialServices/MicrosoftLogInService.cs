using Blocks.Genesis;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;

namespace DomainService.OAuth
{
    public class MicrosoftLogInService : SocialLogInServiceBase
    {
        public MicrosoftLogInService(
            ILogger<MicrosoftLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService
        ) : base(logger, authenticationRepository, cacheClient, httpService)
        {
        }

        protected override IExternalUserData CreateEmptyUserData()
        {
            return new MicrosoftUserData();
        }
    }
}
