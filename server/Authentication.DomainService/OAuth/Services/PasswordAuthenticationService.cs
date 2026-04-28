using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;

namespace DomainService.OAuth
{
    public class PasswordAuthenticationService : ITokenService
    {
        private readonly ILogger<PasswordAuthenticationService> _logger;
        private readonly IOAuthJwtAccessTokenManager _oAuthJwtAccessTokenManager;
        private readonly ITenants _tenants;
        private readonly IAuthenticationRepository _oAuthRepository;
        private readonly ICryptoService _cryptoService;

        public PasswordAuthenticationService(
            ILogger<PasswordAuthenticationService> logger,
            IOAuthJwtAccessTokenManager oAuthJwtAccessTokenManager,
            ITenants tenants,
            ICryptoService cryptoService,
            IAuthenticationRepository oAuthRepository
        )
        {
            _logger = logger;
            _oAuthJwtAccessTokenManager = oAuthJwtAccessTokenManager;
            _tenants = tenants;
            _cryptoService = cryptoService;
            _oAuthRepository = oAuthRepository;
        }
        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            _logger.LogInformation("Password Authentication start");

            user = await _oAuthRepository.GetUserByUsernameAsync(request.Username, request.OrganizationId);
            if (!IsValidUser(user)) return OAuthError.InValidResponse(request);
            if (!IsUserActiveAndVerified(user)) return OAuthError.UserNotActiveOrVerifiedResponse();

            var hashedPassword = HashPassword(request.Password);
            var passwordMatched = user.Password.Equals(hashedPassword);

            if (!passwordMatched)
            {
                return new TokenResponse { Error = OAuthError.InValidUseNamePassword, ErrorDescription = "Invalid username or password", StatusCode = 401 };
            }

            return await _oAuthJwtAccessTokenManager.ManageTokenAsync(request, authenticationConfiguration, user);

        }

        private static bool IsValidUser(User user) =>
            user != null;

        private static bool IsUserActiveAndVerified(User user) =>
            user.Active && user.IsVarified;

        public string HashPassword(string password)
        {
            var sc = BlocksContext.GetContext();
            var tenant = _tenants.GetTenantByID(sc?.TenantId);
            return _cryptoService.Hash(password, tenant?.TenantSalt);
        }
    }
}
