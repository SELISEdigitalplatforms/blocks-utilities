using Blocks.Genesis;
using FluentValidation;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OtpNet;
using QRCoder;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Mfa.DomainService.TOTP
{
    public class TotpService : IOtpService
    {
        private readonly IMfaManagementRepository _repository;
        private readonly ILogger<TotpService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;
        private readonly ICacheClient _cacheClient;
        private readonly IValidator<VerifyOtpRequest> _validator;
        private readonly ITenants _tenant;

        private HttpClient _httpClient;
        const long _defaultTotpLoginSession = 15 * 60;

        public TotpService(IMfaManagementRepository repository,
                           ILogger<TotpService> logger,
                           IHttpContextAccessor httpContextAccessor,
                           IConfiguration configuration,
                           ICacheClient cacheClient,
                           IValidator<VerifyOtpRequest> validator,
                           ITenants tenant)
        {
            _repository = repository;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
            _cacheClient = cacheClient;
            _validator = validator;
            _tenant = tenant;
        }

        public async Task<OtpGenerationResponse> GenerateAsync(UserInfo userInfo, string? sendPhoneNumberAsEmailDomain = null)
        {
            var mfaId = Guid.NewGuid().ToString();
            await _cacheClient.AddStringValueAsync(mfaId, userInfo.ItemId, _defaultTotpLoginSession);
            return new OtpGenerationResponse { IsSuccess = true, MfaId = mfaId };
        }

        public async Task<SetUpUserTotpResponse> GenerateTotpImageByUserAsync(string userId)
        {
            var userInfo = await _repository.GetItemAsync<UserInfo>(u => u.ItemId == userId, "Users");

            if(userInfo is null) { return new SetUpUserTotpResponse { Errors = new Dictionary<string, string> { { "user_not_exist", $"No user exist with useId: {userId}" } } }; }

            var secret = ExtractUserSecret(Guid.NewGuid().ToString());
            var existingOtpInfo = await GetExistingOtpInfoAsync(userId);

            if (existingOtpInfo is not null && !string.IsNullOrWhiteSpace(existingOtpInfo.ImageUri))
            {
                return new SetUpUserTotpResponse { IsSuccess = true, QrImageUrl = existingOtpInfo.ImageUri, QrCode = existingOtpInfo.Secret};
            }

            var fileId = GenerateGuid();
            var twoFactorId = GenerateGuid();
            var preSignedUrl = await GetPreSignedUrlAsync(fileId);
            var applicationDomain = _tenant.GetTenantByID(BlocksContext.GetContext()?.TenantId ?? "")?.ApplicationDomain?.Replace("https://", "") ?? "";

            if (string.IsNullOrWhiteSpace(preSignedUrl)) { return new SetUpUserTotpResponse { Errors = new Dictionary<string, string> { { "configuration_not_exit", "please_check_default_storage_configuration" } } }; }

            var qrCodeData = GenerateQrCodeImageData(applicationDomain, userInfo.Email, secret);
            var response = await UploadQrCodeAsync(preSignedUrl, qrCodeData);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                _logger.LogError("QR code upload failed. statusCode: {0}", response.StatusCode);
                return CreateOtpResponse(false);
            }

            var imageUri = await GetFileUriAsync(fileId);

            if (!string.IsNullOrWhiteSpace(imageUri))
            {
                await SaveOtpInfoAsync(userInfo.ItemId, secret, imageUri, fileId, twoFactorId);
                return CreateOtpResponse(true, imageUri, secret);
            }

            return CreateOtpResponse(false);
        }

        private static string ExtractUserSecret(string userId) => string.Concat(userId.Where(char.IsLetter));
        private static string GenerateGuid() => Guid.NewGuid().ToString();

        private async Task<UserTotpDetail?> GetExistingOtpInfoAsync(string userId)
        {
            return (await _repository.GetItemAsync<UserTotpDetail>(t => t.CreatedBy == userId));
        }

        private async Task<string> GetPreSignedUrlAsync(string fileId)
        {
            var url = _configuration["PreSignedUriForUpload"];

            var requestBody = new
            {
                ItemId = fileId,
                MetaData = "{\"Title\":{\"Type\":\"String\",\"Value\":\"QrImage.png\"},\"OriginalName\":{\"Type\":\"String\",\"Value\":\"image\"}}",
                Name = "QrImage.png",
                Tags = "[\"File\"]",
                ParentDirectoryId = string.Empty,
                AccessModifier = "Public",
                ProjectKey = BlocksContext.GetContext()?.TenantId
            };

            var response = await SendAuthorizedRequestAsync(HttpMethod.Post, url, requestBody);
            return response.GetProperty("uploadUrl").GetString() ?? string.Empty;
        }

        private async Task<string> GetFileUriAsync(string fileId)
        {
            var projectKey = BlocksContext.GetContext()?.TenantId ?? "";
            projectKey = !string.IsNullOrWhiteSpace(projectKey) ? $"&ProjectKey={projectKey}" : "";
            var url = $"{_configuration["GetFileEnpPoint"]}{fileId}{projectKey}";
            var response = await SendAuthorizedRequestAsync(HttpMethod.Get, url);
            return response.GetProperty("url").GetString() ?? string.Empty;
        }

        private async Task<HttpResponseMessage> UploadQrCodeAsync(string preSignedUrl, byte[] qrCodeData)
        {
            _httpClient = new HttpClient();

            using var request = new HttpRequestMessage(HttpMethod.Put, preSignedUrl)
            {
                Content = new ByteArrayContent(qrCodeData)
            };

            request.Headers.Add("x-ms-blob-type", "BlockBlob");

            return await _httpClient.SendAsync(request);
        }

        private async Task SaveOtpInfoAsync(string userId, string secret, string imageUri, string fileId, string twoFactorId)
        {
            var otpInfo = new UserTotpDetail
            {
                CreatedBy = userId,
                LastUpdatedBy = userId,
                Secret = secret,
                ImageUri = imageUri,
                CreatedDate = DateTime.UtcNow,
                LastUpdatedDate = DateTime.UtcNow,
                TowFactorId = twoFactorId,
                ItemId = fileId
            };

            await _repository.SaveAsync(otpInfo);
        }

        private async Task<JsonElement> SendAuthorizedRequestAsync(HttpMethod method, string url, object? content = null)
        {
             var tokenResponse = TokenHelper.GetToken(_httpContextAccessor.HttpContext.Request, _tenant);
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("X-Blocks-Key", GetBlocksKey());
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.Token);

            HttpRequestMessage request = new(method, url);

            if (content is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(content), Encoding.UTF8, "application/json");
            }

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        private string GetBlocksKey()
        {
            return _httpContextAccessor.HttpContext?.Request.Headers["x-blocks-key"].ToString() ?? "";
        }

        private static byte[] GenerateQrCodeImageData(string issuer, string email, string secret)
        {
            string tOTPUri = $"otpauth://totp/{issuer}:{email}?secret={secret}";
            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(tOTPUri, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrCodeData);
            return qrCode.GetGraphic(20);
        }

        private static SetUpUserTotpResponse CreateOtpResponse(bool isSuccess, string imageUri = "", string code ="")
        {
            return new SetUpUserTotpResponse
            {
                IsSuccess = isSuccess,
                QrImageUrl = imageUri,
                QrCode = code
            };
        }

        public async Task<OtpVerificationResponse> VerifyAsync(VerifyOtpRequest request)
        {
            var validator = await _validator.ValidateAsync(request);

            if (!validator.IsValid)
            {
                return new OtpVerificationResponse { Errors = validator.Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage) };
            }

            if (!await _cacheClient.KeyExistsAsync(request.MfaId))
            {
                return new OtpVerificationResponse { Errors = new Dictionary<string, string> { { "login_session_expired", "User login session expire" } } };
            }

            var userId = await _cacheClient.GetStringValueAsync(request.MfaId);
            var tOTPInfo = (await _repository.GetItemAsync<UserTotpDetail>(t => t.CreatedBy == userId));
            var key = Base32Encoding.ToBytes(tOTPInfo?.Secret);
            var tOTP = new Totp(key);
            bool isValid = tOTP.VerifyTotp(request.VerificationCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay);

            return new OtpVerificationResponse { IsSuccess = true, IsValid = isValid, UserId = tOTPInfo.CreatedBy };
        }
    }
}
