using Blocks.Genesis;
using DomainService.Entities;
using Iam.DomainService.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DomainService.OAuth
{
    public class JwtAccessTokenProvider : IJwtAccessTokenProvider
    {
        private readonly ILogger<JwtAccessTokenProvider> _logger;
        private readonly IDatabase _cacheDb;
        private readonly ICryptoService _cryptoService;
        private readonly ICertificateProviderFactory _certificateProviderFactory;
        private string _key;


        public JwtAccessTokenProvider(
            ILogger<JwtAccessTokenProvider> logger,
            ICacheClient cacheClient,
            ICryptoService cryptoService,
            ICertificateProviderFactory certificateProviderFactory
        )
        {
            _logger = logger;
            _cacheDb = cacheClient.CacheDatabase();
            _cryptoService = cryptoService;
            _certificateProviderFactory = certificateProviderFactory;

        }

        public async Task<JwtAccessToken> GetJwtAccessToken(AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user, StateInfo? state = null, string? organizationId = null)
        {
            _key = _cryptoService.Hash(Encoding.UTF8.GetBytes($"{tenant.TenantId}::{tenant.ItemId}"));
            var certificate = await GetOrRetrieveCertAsync(tenant);
            if (certificate == null) return new JwtAccessToken();
            return MapJwtAccessToken(authenticationConfiguration, tenant, user, certificate, stateInfo: state, organizationId: organizationId);
        }

        public JwtAccessToken MapJwtAccessToken(AuthenticationConfiguration authenticationConfiguration, Tenant tenant, User user, byte[] certificate, StateInfo? stateInfo = null, string? organizationId = null)
        {
            var jwtAccessToken = new JwtAccessToken
            {
                RefreshTokenValidForNumberMinute = authenticationConfiguration.RefreshTokenValidForNumberMinutes,
                AccessTokenValidForNumberMinute = authenticationConfiguration.AccessTokenValidForNumberMinutes,
                RememberMeRefreshTokenValidForNumberMinute = authenticationConfiguration.RememberMeRefreshTokenValidForNumberMinutes,
                Issuer = tenant.JwtTokenParameters.Issuer,
                Audience = string.Join(",", tenant.JwtTokenParameters.Audiences),
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddMinutes(authenticationConfiguration.AccessTokenValidForNumberMinutes),
                SigningCredentials = MakeSigningCredentials(certificate, tenant.JwtTokenParameters.PrivateCertificatePassword)
            };

            var claimsIdentity = new ClaimsIdentity("seliseblocks-authentication");
            AddClaims(claimsIdentity, tenant, user, stateInfo: stateInfo, organizationId: organizationId);
            jwtAccessToken.Claims = claimsIdentity.Claims;

            return jwtAccessToken;
        }

        public static void AddClaims(ClaimsIdentity claimsIdentity, Tenant tenant, User user, StateInfo? stateInfo = null, string? organizationId = null)
        {
            claimsIdentity.AddClaim(new Claim(BlocksContext.TENANT_ID_CLAIM, tenant.TenantId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.SUBJECT_CLAIM, $"blocks|{user.ItemId}"));
            claimsIdentity.AddClaim(new Claim(BlocksContext.USER_ID_CLAIM, user.ItemId));
            claimsIdentity.AddClaim(new Claim(BlocksContext.ISSUED_AT_TIME_CLAIM, EpochTime.GetIntDate(DateTime.UtcNow).ToString(), ClaimValueTypes.Integer64));
            claimsIdentity.AddClaim(new Claim(BlocksContext.ORGANIZATION_ID_CLAIM, user.Memberships.Any(org => org.OrganizationId == organizationId) ? organizationId : "default"));
            claimsIdentity.AddClaim(new Claim(BlocksContext.EMAIL_CLAIM, user.Email));
            claimsIdentity.AddClaim(new Claim(BlocksContext.USER_NAME_CLAIM, user.UserName));
            claimsIdentity.AddClaim(new Claim(BlocksContext.DISPLAY_NAME_CLAIM, $"{user.FirstName ?? string.Empty} {user.LastName ?? string.Empty}".Trim()));
            claimsIdentity.AddClaim(new Claim(BlocksContext.PHONE_NUMBER_CLAIM, user.PhoneNumber ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(stateInfo?.Nonce)) 
            {
                claimsIdentity.AddClaim(new Claim("nonce", stateInfo?.Nonce ?? ""));
            }

            foreach (var role in user.Memberships.Where(m => m.OrganizationId == (!string.IsNullOrWhiteSpace(organizationId)? organizationId: "default")).FirstOrDefault()?.Roles ?? [])
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.ROLES_CLAIM, role));
            }

            foreach (var permission in user.Memberships.Where(m => m.OrganizationId == (!string.IsNullOrWhiteSpace(organizationId) ? organizationId : "default")).FirstOrDefault()?.Permissions ?? [])
            {
                claimsIdentity.AddClaim(new Claim(BlocksContext.PERMISSION_CLAIM, permission));
            }
        }
        
        public async Task<byte[]?> GetOrRetrieveCertAsync(Tenant tenant)
        {
            _logger.LogInformation("Getting Certificate");
            var cachedCert = _cacheDb.StringGet(_key);
            _logger.LogInformation("Has Cache Certificate: {CC}", cachedCert.HasValue);

            if (!cachedCert.HasValue)
            {
                var provider = _certificateProviderFactory.GetProvider(tenant.JwtTokenParameters?.CertificateStorageType ?? CertificateStorageType.Azure);
                var certificate = await provider.GetCertificateAsync(_key);

                if (certificate != null && certificate.Length > 0)
                {
                    var expirationDays = tenant.JwtTokenParameters?.CertificateValidForNumberOfDays - (DateTime.UtcNow - tenant.JwtTokenParameters?.IssueDate)?.Days - 1;
                    _cacheDb.StringSet(_key, certificate, TimeSpan.FromDays(expirationDays ?? 0));
                    _logger.LogInformation("Certificate set to cache");
                }
                return certificate;
            }

            return cachedCert;
        }
        
        public static SigningCredentials MakeSigningCredentials(byte[] certificateData, string password)
        {
            X509Certificate2 certificate;

            try
            {
                certificate = X509CertificateLoader.LoadPkcs12(certificateData, password, X509KeyStorageFlags.Exportable);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PKCS12 certificate loading failed: {ex.Message}. Trying fallback loader...");

                try
                {
                    certificate = X509CertificateLoader.LoadCertificate(certificateData);
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"Fallback certificate loading failed: {fallbackEx.Message}");
                    throw new InvalidOperationException("Failed to load X509 certificate from provided data.", fallbackEx);
                }
            }

            var rsa = certificate.GetRSAPrivateKey() ?? throw new CryptographicException("Invalid private key");
            // Create the security key from the full RSA key parameters
            _ = new RsaSecurityKey(rsa)
                    {
                        // *** THIS IS THE CRITICAL STEP ***
                        // Set the KeyId on the key object using the same logic as the JWKS endpoint.
                        KeyId = Base64UrlEncoder.Encode(certificate.Thumbprint)
                    };
            return new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256, SecurityAlgorithms.Sha256Digest);
        }
    }
}
