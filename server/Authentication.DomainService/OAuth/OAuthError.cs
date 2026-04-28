using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using Microsoft.AspNetCore.Mvc;

namespace DomainService.OAuth
{
    public static class OAuthError
    {
        public const string AccountLocked = "account_locked";
        public const string MfaEnabled = "mfa_enabled";
        public const string CaptchaEnabled = "captcha_enabled";
        public const string InValidUseNamePassword = "invalid_username_password";
        public const string UserInActiveOrNotVerified = "user_inactive_or_not_verified";
        public const string RefreshTokenCookieNotFound = "refresh_token_not_found_in_cookie";
        public const string InvalidRefreshToken = "invalid_refresh_token";

        public static IActionResult InvalidRequest(string description = "The request is missing a required parameter.", string state = "")
        {
            return new BadRequestObjectResult(new
            {
                error = "invalid_request",
                error_description = description,
                state
            });
        }

        public static IActionResult UnsupportedGrantType(string state = "")
        {
            return new BadRequestObjectResult(new
            {
                error = "unsupported_grant_type",
                error_description = "The grant type is not supported.",
                state
            });
        }

        public static IActionResult UnauthorizedClient(string state = "")
        {
            return new BadRequestObjectResult(new
            {
                error = "unauthorized_client",
                error_description = "The client is not authorized to request an authorization code using this method.",
                state
            });
        }

        public static IActionResult Error400Response(string error, string error_description)
        {
            return new BadRequestObjectResult(new
            {
                error,
                error_description
            });
        }

        public static IActionResult Error401Response(string error, string error_description)
        {
            return new UnauthorizedObjectResult(new
            {
                error,
                error_description
            });
        }

        public static TokenResponse InValidResponse(TokenRequest request)
        {
            return request.GrantType switch
            {
                GrantTypes.Password => new TokenResponse
                {
                    Error = InValidUseNamePassword,
                    ErrorDescription = "User name or password invalid",
                    StatusCode = 401
                },
                GrantTypes.MfaCode => new TokenResponse
                {
                    Error = "invalid_request_body",
                    ErrorDescription = "Code, two_factor_id and mfa_type should not be empty",
                    StatusCode = 400
                },
                GrantTypes.AuthCode => new TokenResponse
                {
                    Error = "invalid_request_body",
                    ErrorDescription = "Code, auth code required",
                    StatusCode = 400
                },

                GrantTypes.SsoConsentCode => new TokenResponse
                {
                    Error = "invalid_request_body",
                    ErrorDescription = "Code, sso consent code required",
                    StatusCode = 400
                },

                _ => new TokenResponse
                {
                    Error = "invalid_grant_type",
                    ErrorDescription = "Unsupported grant type provided",
                    StatusCode = 400
                }
            };
        }


        public static TokenResponse UserNotActiveOrVerifiedResponse()
        {
            return new TokenResponse
            {
                Error = UserInActiveOrNotVerified,
                ErrorDescription = "User is not active or verified",
                StatusCode = 400
            };
        }

        public static TokenResponse InValidOrganization(string organization)
        {
            return new TokenResponse
            {
                Error = UserInActiveOrNotVerified,
                ErrorDescription = $"User is not exist within {organization} organization",
                StatusCode = 400
            };
        }
    }
}
