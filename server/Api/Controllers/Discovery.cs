using Blocks.Genesis;
using DomainService.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography.X509Certificates;

namespace Api.Controllers
{
    [ApiController]
    [Route(".well-known")]

    public class DiscoveryController : ControllerBase
    {
        private readonly ICacheClient _cacheClient;
        private readonly ITenants _tenants;
        private readonly IConfiguration _configuration;

        public DiscoveryController(ICacheClient cacheClient,
                                   ITenants tenants,
                                   IConfiguration configuration)
        {
            _cacheClient = cacheClient;
            _tenants = tenants;
            _configuration = configuration;
        }

        // This method provides the JWKS
        [HttpGet("jwks.json")] // This makes the full path /.well-known/jwks.json
        [Produces("application/json")]
        public async Task<IActionResult> GetJwks([FromQuery] string? projectKey)
        {
            projectKey = !string.IsNullOrWhiteSpace(projectKey) ? projectKey : BlocksContext.GetContext().TenantId;
            string cacheKey = $"{IdpConstants.TenantTokenPublicCertificateCachePrefix}{projectKey}";
            var certData = await _cacheClient.CacheDatabase().StringGetAsync(cacheKey);
            X509Certificate2 certificate;

            try
            {
                certificate = X509CertificateLoader.LoadPkcs12(certData, _tenants.GetTenantTokenValidationParameter(BlocksContext.GetContext().TenantId).PublicCertificatePassword);
            }
            catch (Exception ex)
            {
                //Fallback
                certificate = X509CertificateLoader.LoadCertificate(certData);
            }

            // Create SecurityKey from certificate
            var rsaPublicKey = certificate.GetRSAPublicKey();

            // Step 3: Export the public key parameters.
            var rsaParameters = rsaPublicKey.ExportParameters(false); // false for public key only

            // Step 4: Manually and explicitly build the JsonWebKey.
            var jwk = new JsonWebKey
            {
                Kty = "RSA",
                // The standard requires these values to be Base64UrlEncoded.
                N = Base64UrlEncoder.Encode(rsaParameters.Modulus),
                E = Base64UrlEncoder.Encode(rsaParameters.Exponent),
                // Use the thumbprint for a stable, unique Key ID.
                Kid = Base64UrlEncoder.Encode(certificate.Thumbprint),
                // These metadata fields are best practice.
                Use = "sig", // This key is for verifying signatures.
                Alg = SecurityAlgorithms.RsaSha256
            };

            // Step 5: Return the JWKS structure.
            return Ok(new { keys = new[] { jwk } });

        }

        // Example: If you also need the OpenID Connect configuration
        [HttpGet("openid-configuration")]
        [Produces("application/json")]
        public IActionResult GetOpenIdConfiguration([FromQuery] string? projectKey)
        {
            var issuerUri = _configuration["OpenIdConnect:IssuerUri"] ?? "https://your-idp.com"; // Get from config
            string apiKey = !string.IsNullOrWhiteSpace(projectKey) ? projectKey : BlocksContext.GetContext().TenantId ?? "";

            var config = new
            {
                issuer = issuerUri,
                authorization_endpoint = $"{issuerUri}/Authentication/authorize?X-Blocks-Key={apiKey}",
                token_endpoint = $"{issuerUri}/Authentication/token?X-Blocks-Key={apiKey}",
                userinfo_endpoint = $"{issuerUri}/Authentication/GetUserInfo?X-Blocks-Key={apiKey}",
                jwks_uri = $"{issuerUri}/.well-known/jwks.json?X-Blocks-Key={apiKey}",
                response_types_supported = new[] { "code" },
                subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" },
                scopes_supported = new[] { "openid", "email", "profile" },
                token_endpoint_auth_methods_supported = new[] { "client_secret_basic",
                                                                "client_secret_post",
                                                                "client_secret_jwt",
                                                                 "private_key_jwt",
                                                                 "none" }
            };
            return Ok(config);
        }
    }
}
