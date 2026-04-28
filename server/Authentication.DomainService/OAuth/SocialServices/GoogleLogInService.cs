using Blocks.Genesis;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using Microsoft.Extensions.Logging;

namespace DomainService.OAuth
{
    public class GoogleLogInService : SocialLogInServiceBase
    {
        public GoogleLogInService(
            ILogger<GoogleLogInService> logger,
            IAuthenticationRepository authenticationRepository,
            ICacheClient cacheClient,
            IHttpService httpService
        ) : base(logger, authenticationRepository, cacheClient, httpService)
        {
        }

        protected override IExternalUserData CreateEmptyUserData()
        {
            return new GoogleUserData();
        }
    }
}
