using Blocks.Genesis;
using DeviceDetectorNET;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.RequestModel;
using DomainService.ResponseModel;
using DomainService.Shared;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Text.Json;
using DomainService.Shared.ResponseModel;
using FluentValidation;
using DomainService.Shared.RequestModel;
using Iam.DomainService.Dtos;


namespace DomainService.Services
{
    public class AuthenticationDomainService : IAuthenticationDomainService
    {
        private readonly IMessageClient _messageClient;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly IConfiguration _configuration;
        private readonly IUserRepository _userRepository;
        private readonly IValidator<SaveSsoCredentialRequest> _validator;
        private readonly ITenants _tenants;


        private readonly static HttpClient _httpClient = new();

        private const string Origin_Header_Name = "Origin";
        private const string Referer_Header_Name = "Referer";
        private const string X_Forwarded_For_Header_Name = "X-Forwarded-For";

        public AuthenticationDomainService(IMessageClient messageClient,
                                           IAuthenticationRepository authenticationRepository,
                                           IConfiguration configuration,
                                           IUserRepository userRepository,
                                           IValidator<SaveSsoCredentialRequest> validator,
                                           ITenants tenants)
        {
            _messageClient = messageClient;
            _authenticationRepository = authenticationRepository;
            _configuration = configuration;
            _userRepository = userRepository;
            _validator = validator;
            _tenants = tenants;
        }

        public IEnumerable<string> GetVisitorsIpAddresses(HttpContext context)
        {
            var forwardedForHeader = context.Request.Headers[X_Forwarded_For_Header_Name];

            var visitorsIpAddress = string.IsNullOrWhiteSpace(forwardedForHeader) ? context.Connection.RemoteIpAddress.ToString() : forwardedForHeader.ToString();

            var visitorsIpAddresses =
                visitorsIpAddress
               .Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(ipAddress => ipAddress.Trim());

            return visitorsIpAddresses;
        }

        public string GetRequestOriginHostName(HttpContext context)
        {
            var originHeaderValue = context.Request.Headers[Origin_Header_Name];

            if (!string.IsNullOrWhiteSpace(originHeaderValue))
            {
                return new Uri(originHeaderValue).Host;
            }

            var refererHeaderValue = context.Request.Headers[Referer_Header_Name];

            if (!string.IsNullOrWhiteSpace(refererHeaderValue))
            {
                return new Uri(refererHeaderValue).Host;
            }

            return string.Empty;
        }

        public async Task SendToQueueAsync<T>(string queue, T payload) where T : class
        {
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<T>
            {
                ConsumerName = queue,
                Payload = payload
            });
        }

        public async Task SendToTopicAsync<T>(string queue, T payload) where T : class
        {
            await _messageClient.SendToMassConsumerAsync(new ConsumerMessage<T>
            {
                ConsumerName = queue,
                Payload = payload
            });
        }

        public DeviceInformation? GetDeviceInfo(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent)) return null;

            // Initialize the DeviceDetector with the User-Agent string
            var deviceDetector = new DeviceDetector(userAgent);
            deviceDetector.Parse();

            // Retrieve device details
            var clientInfo = deviceDetector.GetClient();
            var osInfo = deviceDetector.GetOs();

            return new DeviceInformation
            {
                Browser = clientInfo?.Match?.Name ?? string.Empty,
                OS = osInfo?.Match?.Name ?? string.Empty,
                Device = deviceDetector.GetDeviceName(),
                Brand = deviceDetector.GetBrandName(),
                Model = deviceDetector.GetModel()
            };
        }

        public async Task<SaveSsoCredentialResponse> SaveSocialLoginCredentialAsync(SaveSsoCredentialRequest credential)
        {
            var validationResult = await _validator.ValidateAsync(credential);

            if (!validationResult.IsValid)
            {
                return new SaveSsoCredentialResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
                };
            }

            var loginCredential = await _authenticationRepository.GetSocialLoginCredentialByIdAsync(credential?.ItemId ?? "");
            var repoCredential = await MapToSocialLoginCredential(loginCredential, credential);
            await _authenticationRepository.SaveSocialLoginCredentialAsync(repoCredential);

            return new SaveSsoCredentialResponse { IsSuccess = true, ItemId = repoCredential.ItemId };
        }

        public static async Task<OpenIdConnectConfiguration?> GetMetadataAsync(string wellKnownUrl)
        {
            var response = await _httpClient.GetAsync(wellKnownUrl);
            string json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<OpenIdConnectConfiguration>(json);
        }

        public async Task<BaseResponse> DeleteSocialLoginCredentialAsync(string itemId)
        {
            await _authenticationRepository.DeleteSocialLoginCredentialAsync(itemId);
            return new BaseResponse { IsSuccess = true, };
        }

        public async Task<GetSsoCredentialResponse> GetSsoCredentialAsync(string itemId)
        {
            var credential = await _authenticationRepository.GetSocialLoginCredentialByIdAsync(itemId);

            var roles = await _userRepository.GetRolesBySlugsAsync(credential.InitialRoles);
            var permissions = await _userRepository.GetPermissionsByResourcesAsync(credential.InitialPermissions);

            var response = GetResponse(credential);
            response.UserRoles = roles;
            response.UserPermissions = permissions;

            return response;
        }

        public async Task<SaveOIDCClientResponse> SaveOIDCClientAsync(SaveOIDCClientRequest request)
        {
            var credential = await _authenticationRepository.GetOIDCClientCredentialAsync(request.ItemId ?? "");

            credential = credential ?? new OIDCClientCredential
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedBy = BlocksContext.GetContext()?.UserId,
                CreatedDate = DateTime.UtcNow,
            };

            credential.ClientSecret = Guid.NewGuid().ToString("n");
            credential.Audience = request.Audience;
            credential.RedirectUri = request.RedirectUri;
            credential.Scope = request.Scope;
            credential.IsAutoRedirect = request.IsAutoRedirect;
            credential.LastUpdatedBy = BlocksContext.GetContext()?.UserId;
            credential.LastUpdatedDate = DateTime.UtcNow;
            credential.ClientLogoUrl = request.ClientLogoUrl;
            credential.ClientDisplayName = request.ClientDisplayName;
            credential.ClientBrandColor = request.ClientBrandColor;
            await _authenticationRepository.SaveOIDCClientCredentialAsync(credential);
            return new SaveOIDCClientResponse { IsSuccess = true, ItemId = credential.ItemId };
        }

        public async Task<GetOIDCClientResponse> GetOIDCClientAsyncAsync(string tenantId)
        {
            var client = await _authenticationRepository.GetOIDCCredentialByIdAsync(tenantId);

            return new GetOIDCClientResponse
            {
                oIDCClientCredential = client,
                IsSuccess = true
            };
        }

        public async Task<GetOIDCClientsResponse> GetOIDCClientsAsyncAsync()
        {
            var clients = await _authenticationRepository.GetOIDCCredentialsByTenantAsync();

            return new GetOIDCClientsResponse
            {
                oIDCClientCredentials = clients ?? [],
                IsSuccess = true
            };
        }

        public async Task<BaseResponse> DeleteOIDCClientAsyncAsync(DeleteOIDCClientRequest request)
        {
            await _authenticationRepository.DeleteOidcCliantAsync(request);

            return new BaseResponse { IsSuccess = true };
        }

        private GetSsoCredentialResponse GetResponse(SocialLoginCredential socialLoginCredential)
        {
            return new GetSsoCredentialResponse
            {
                Audience = socialLoginCredential.Audience,
                ClientId = socialLoginCredential.ClientId,
                ClientSecret = socialLoginCredential.ClientSecret,
                Provider = socialLoginCredential.Provider,
                RedirectUrl = socialLoginCredential.RedirectUrl,
                AuthorizationUrl = socialLoginCredential.AuthorizationUrl,
                WellKnownUrl = socialLoginCredential.WellKnownUrl,
                TokenUrl = socialLoginCredential.TokenUrl,
                GetProfileUrl = socialLoginCredential.GetProfileUrl,
                Scope = socialLoginCredential.Scope,
                ItemId = socialLoginCredential.ItemId,
                CreatedBy = socialLoginCredential.CreatedBy,
                LastUpdatedBy = socialLoginCredential.LastUpdatedBy,
                CreatedDate = socialLoginCredential.CreatedDate,
                LastUpdatedDate = socialLoginCredential.LastUpdatedDate
            };
        }

        private async Task<SocialLoginCredential> MapToSocialLoginCredential(SocialLoginCredential credential, SaveSsoCredentialRequest saveSocialLoginCredentialRequest)
        {
            var now = DateTime.UtcNow;
            var userId = BlocksContext.GetContext().UserId;
            var metaData = !string.IsNullOrWhiteSpace(saveSocialLoginCredentialRequest.WellKnownUrl) ? await GetMetadataAsync(saveSocialLoginCredentialRequest.WellKnownUrl) : null;

            credential ??= new SocialLoginCredential
            {
                ItemId = Guid.NewGuid().ToString(),
                CreatedBy = userId,
                CreatedDate = now,
                LastUpdatedDate = now,
                LastUpdatedBy = userId,
                ClientSecret = saveSocialLoginCredentialRequest.ClientSecret,
                ClientId = saveSocialLoginCredentialRequest.ClientId,
                Provider = saveSocialLoginCredentialRequest.Provider,
                Audience = saveSocialLoginCredentialRequest.Audience,
                AuthorizationUrl = metaData?.AuthorizationEndpoint ?? _configuration[$"{saveSocialLoginCredentialRequest.Provider}:AuthorizationUrl"] ?? "",
                TokenUrl = metaData?.TokenEndpoint ?? _configuration[$"{saveSocialLoginCredentialRequest.Provider}:TokenUrl"] ?? "",
                GetProfileUrl = metaData?.UserInfoEndpoint ?? _configuration[$"{saveSocialLoginCredentialRequest.Provider}:GetProfileUrl"] ?? "",
                GetEmailUrl = _configuration[$"{saveSocialLoginCredentialRequest.Provider}:GetEmailUrl"] ?? "",
                RedirectUrl = saveSocialLoginCredentialRequest.RedirectUrl,
                WellKnownUrl = saveSocialLoginCredentialRequest.WellKnownUrl,
                Scope = metaData?.ScopesSupported.ToString() ?? _configuration[$"{saveSocialLoginCredentialRequest.Provider}:Scope"] ?? ""
            };

            credential.Audience = saveSocialLoginCredentialRequest.Audience;
            credential.ClientId = saveSocialLoginCredentialRequest.ClientId;
            credential.ClientSecret = saveSocialLoginCredentialRequest.ClientSecret;
            credential.RedirectUrl = saveSocialLoginCredentialRequest.RedirectUrl;
            credential.WellKnownUrl = saveSocialLoginCredentialRequest.WellKnownUrl;
            credential.Provider = saveSocialLoginCredentialRequest.Provider;
            credential.LastUpdatedDate = now;
            credential.LastUpdatedBy = userId;
            credential.InitialRoles = saveSocialLoginCredentialRequest.InitialRoles;
            credential.InitialPermissions = saveSocialLoginCredentialRequest.InitialPermissions;
            credential.IsDisabled = saveSocialLoginCredentialRequest.IsDisabled;
            credential.SSOType = saveSocialLoginCredentialRequest.SSOType;
            credential.TeamId = saveSocialLoginCredentialRequest.TeamId;
            credential.KeyId = saveSocialLoginCredentialRequest.KeyId;
            credential.PrivateKey = saveSocialLoginCredentialRequest.PrivateKey;
            credential.AppleAudience = _configuration[$"{saveSocialLoginCredentialRequest.Provider}:AppleAudience"] ?? "";

            return credential;
        }

        public async Task<List<SocialLoginCredential>> GetSocialLoginCredentialsAsync()
        {
            return await _authenticationRepository.GetSocialLoginCredentialsAsync();
        }

        public async Task<BaseResponse> UpdateSsoCredentialStatusAsync(UpdateSsoCredentialStatusRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ItemId))
            {
                return new BaseResponse { Errors = new Dictionary<string, string> { { "empty_item_id", "ItemId should not be empty" } } };
            }

            var updates = new Dictionary<string, object>
                          {
                             { nameof(SocialLoginCredential.IsDisabled), request.IsEnabled }
                          };

            await _authenticationRepository.UpdatePartialAsync<SocialLoginCredential>(request.ItemId, updates, "SocialLoginCredentials");

            return new BaseResponse { IsSuccess = true };
        }

        public async Task<BaseResponse> GenerateUserCodeByClientAsync(GenerateUserCodeRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ClientId))
            {
                return new BaseResponse { Errors = new Dictionary<string, string> { { "invalid_request", "ClientId is required." } } };
            }

            var userCode = Guid.NewGuid().ToString("n");

            var clientUserCode = new UserCode
            {
                ItemId = Guid.NewGuid().ToString(),
                ClientId = request.ClientId,
                UserId = BlocksContext.GetContext()?.UserId,
                Code = userCode,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedBy = BlocksContext.GetContext()?.UserId,
                CreatedDate = DateTime.UtcNow,
                CodeTtlInMinute = request.CodeTtlInMinute,
                Note = request.Note
            };

            await _authenticationRepository.SaveUserCodeByClientAsync(clientUserCode);
            return new BaseResponse { IsSuccess = true, };
        }

        public async Task<BaseResponse> SaveClientCredentialAsync(SaveClientCredentialRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return new BaseResponse { IsSuccess = false, Errors = new Dictionary<string, string> { { "invalid_request", "Name is required." } } };

            var clientCredential = new ClientCredential
            {
                ItemId = Guid.NewGuid().ToString(),
                ClientSecret = Guid.NewGuid().ToString("n"),
                Name = request.Name,
                CreatedBy = BlocksContext.GetContext()?.UserId,
                LastUpdatedBy = BlocksContext.GetContext()?.UserId,
                CreatedDate = DateTime.UtcNow,
                Roles = request.Roles,
                IsActive = true,
                Audiences = _tenants.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? "")?.JwtTokenParameters?.Audiences ?? []
            };

            return await _authenticationRepository.SaveClientCredentialAsync(clientCredential);
        }

        public async Task<BaseResponse> DeleteClientCredentialAsync(DeleteClientCredentialRequest request)
        {
            await _authenticationRepository.DeleteClientCredentialAsync(request);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<List<ClientCredential>> GetClientCredentialsAsync(GetAllClientCredentialsRequest request)
        {
            return await _authenticationRepository.GetClientCredentialsAsync();
        }
    }
}
