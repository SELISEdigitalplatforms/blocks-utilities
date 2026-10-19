using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Utility.DomainService.MagicLink.Models;
using Utility.DomainService.MagicLink.Utilities;

namespace Utility.DomainService.MagicLink.Service
{
    /// <summary>
    /// Response from OAuth token endpoint
    /// </summary>
    public class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
    }

    /// <summary>
    /// Service interface for obtaining authentication tokens using client credentials
    /// </summary>
    public interface IClientCredentialTokenService
    {
        /// <summary>
        /// Gets an access token using client credentials
        /// </summary>
        /// <param name="clientCredentials">The client credentials entity</param>
        /// <param name="projectKey">The project key (used as X-Blocks-Key header)</param>
        /// <returns>The access token or null if failed</returns>
        Task<string?> GetTokenAsync(ClientCredential clientCredentials, string projectKey);
    }

    /// <summary>
    /// Service implementation for obtaining authentication tokens using client credentials
    /// </summary>
    public class ClientCredentialTokenService : IClientCredentialTokenService
    {
        private readonly ILogger<ClientCredentialTokenService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public ClientCredentialTokenService(
            ILogger<ClientCredentialTokenService> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        /// <summary>
        /// The IdP endpoint tokens are minted from. Its host is also the only host a minted token
        /// may be sent to (see <see cref="MagicLinkActionExecutor.MayReceiveToken"/>).
        /// </summary>
        public static string ResolveTokenEndpoint(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            return configuration["AuthenticationTokenEndpoint"] ?? MagicLinkConstants.DefaultAuthenticationTokenEndpoint;
        }

        public async Task<string?> GetTokenAsync(ClientCredential clientCredentials, string projectKey)
        {
            try
            {
                _logger.LogInformation("Getting token for ClientId: {ClientId}", clientCredentials.ItemId);

                var authEndpoint = ResolveTokenEndpoint(_configuration);

                using var client = _httpClientFactory.CreateClient();

                // Set headers
                client.DefaultRequestHeaders.Add("X-Blocks-Key", projectKey);

                // Prepare form data
                var formData = new Dictionary<string, string>
                {
                    { "grant_type", "client_credential" },
                    { "client_id", clientCredentials.ItemId },
                    { "client_secret", clientCredentials.ClientSecret }
                };

                var content = new FormUrlEncodedContent(formData);

                // Make the request
                var response = await client.PostAsync(authEndpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get token for ClientId: {ClientId}. Status: {StatusCode}, Error: {Error}",
                        clientCredentials.ItemId, response.StatusCode, errorContent);
                    return null;
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);

                if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
                {
                    _logger.LogError("Token response is empty or invalid for ClientId: {ClientId}", clientCredentials.ItemId);
                    return null;
                }

                _logger.LogInformation("Successfully obtained token for ClientId: {ClientId}, ExpiresIn: {ExpiresIn}s",
                    clientCredentials.ItemId, tokenResponse.ExpiresIn);
                return tokenResponse.AccessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting token for ClientId: {ClientId}", clientCredentials.ItemId);
                return null;
            }
        }
    }
}
