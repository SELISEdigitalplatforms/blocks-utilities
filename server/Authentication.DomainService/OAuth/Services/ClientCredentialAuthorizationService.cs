using Blocks.Genesis;
using DomainService.Entities;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.Services;
using Iam.DomainService.Entities;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace DomainService.OAuth.Services
{
    public class ClientCredentialAuthorizationService : ITokenService
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ICertificateProviderFactory _certificateProviderFactory;
        private readonly ICryptoService _cryptoService;
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        
        public ClientCredentialAuthorizationService(IAuthenticationRepository authenticationRepository,
                                                    ICertificateProviderFactory certificateProviderFactory,
                                                    ICryptoService cryptoService,
                                                    ICacheClient cacheClient,
                                                    ITenants tenants)
        {
            _authenticationRepository = authenticationRepository;
            _certificateProviderFactory = certificateProviderFactory;
            _cryptoService = cryptoService;
            _cacheClient = cacheClient;
            _tenants = tenants;    
        }

        public async Task<TokenResponse> AuthenticateAsync(TokenRequest request, AuthenticationConfiguration authenticationConfiguration, User? user = null)
        {
            var client = await _authenticationRepository.GetClientCredentialByIdAsync(request.ClientId);
            var validationResult = ValidateClient(client, request);

            if (validationResult != null)
                return validationResult;

            var jwtToken = await GetJwtAccessToken(authenticationConfiguration, client);
            var accessToken =  OAuthJwtAccessTokenManager.CreateJwtAccessToken(jwtToken);

            return new TokenResponse { AccessToken = accessToken, ExpiresIn = authenticationConfiguration.AccessTokenValidForNumberMinutes, ExpiresUtc = jwtToken.Expires, StatusCode = 200 };
        }

        private async Task<JwtAccessToken> GetJwtAccessToken(AuthenticationConfiguration authenticationConfiguration, ClientCredential client)
        {
            var tenant = _tenants.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? "");
            var certificate = await RetrievePrivateCertAsync(tenant);
            if (certificate == null) return new JwtAccessToken();
            return MapJwtAccessToken(authenticationConfiguration, tenant, client, certificate);
        }

        public async Task<byte[]?> RetrievePrivateCertAsync(Tenant tenant)
        {
            var _key = _cryptoService.Hash(Encoding.UTF8.GetBytes($"{tenant.TenantId}::{tenant.ItemId}"));
            var cachedCert = _cacheClient.CacheDatabase().StringGet(_key);

            if (!cachedCert.HasValue)
            {
                var provider = _certificateProviderFactory.GetProvider(tenant.JwtTokenParameters?.CertificateStorageType ?? CertificateStorageType.Azure);
                var certificate = await provider.GetCertificateAsync(_key);

                if (certificate.Length > 0)
                {
                    var expirationDays = tenant.JwtTokenParameters?.CertificateValidForNumberOfDays - (DateTime.UtcNow - tenant.JwtTokenParameters?.IssueDate)?.Days - 1;
                    _cacheClient.CacheDatabase().StringSet(_key, certificate, TimeSpan.FromDays(expirationDays ?? 0));
                }

                return certificate;
            }

            return cachedCert;
        }

        private static JwtAccessToken MapJwtAccessToken(AuthenticationConfiguration authenticationConfiguration, Tenant tenant, ClientCredential client, byte[] certificate)
        {
            var jwtAccessToken = new JwtAccessToken
            {
                AccessTokenValidForNumberMinute = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                Issuer = tenant.JwtTokenParameters.Issuer,
                Audience = string.Join(",", tenant.JwtTokenParameters.Audiences),
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddMinutes(authenticationConfiguration.AccessTokenValidForNumberMinutes),
                SigningCredentials = JwtAccessTokenProvider.MakeSigningCredentials(certificate, tenant.JwtTokenParameters.PrivateCertificatePassword)
            };

            var claimsIdentity = new ClaimsIdentity("seliseblocks-authentication");
            AddClaims(claimsIdentity, tenant, client);
            jwtAccessToken.Claims = claimsIdentity.Claims;

            return jwtAccessToken;
        }

        public static void AddClaims(ClaimsIdentity claimsIdentity, Tenant tenant, ClientCredential client)
        {
            claimsIdentity.AddClaim(new Claim(BlocksContext.TENANT_ID_CLAIM, tenant.TenantId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.SUBJECT_CLAIM, $"blocks|{client.ItemId}"));
            claimsIdentity.AddClaim(new Claim("client_id", client.ItemId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.ISSUED_AT_TIME_CLAIM, EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64));

            foreach (var role in client.Roles)
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.ROLES_CLAIM, role));
            }
        }

        private static TokenResponse? ValidateClient(ClientCredential? client, TokenRequest request)
        {
            return client switch
            {
                null => new TokenResponse
                {
                    Error = "invalid_client",
                    ErrorDescription = "No client found"
                },

                _ when request.ClientSecret != client.ClientSecret => new TokenResponse
                {
                    Error = "invalid_client",
                    ErrorDescription = "Client secret not match"
                },

                _ when !client.IsActive => new TokenResponse
                {
                    Error = "invalid_client",
                    ErrorDescription = "Client is not active"
                },

                _ => null 
            };
        }
    }
}
