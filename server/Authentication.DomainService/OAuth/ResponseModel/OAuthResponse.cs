using DomainService.OAuth.RequestModel;
using Microsoft.AspNetCore.Mvc;

namespace DomainService.OAuth.ResponseModel
{
    public static class OAuthResponse
    {
        public static IActionResult TokenResponse(TokenResponse response, TokenRequest request)
        {
            return new OkObjectResult(new
            {
                access_token = response.AccessToken,
                token_type = "Bearer",
                expires_in = response.ExpiresIn,
                refresh_token = response.RefreshToken,
                id_token = request.Scope.Contains("openid") ? response.AccessToken : null
            });
        }

        public static IActionResult SSOUserNotExistResponse(TokenResponse response)
        {
            return new OkObjectResult(new
            {
                sso_user_redirect_url = response.SsoUserRedirectUrl
            });
        }

        public static IActionResult MfaResponse(TokenResponse response)
        {
            return new OkObjectResult(new
            {
                enable_mfa = true,
                message = response.ErrorDescription,
                mfaId = response.MfaId,
                mfaType = response.UserMfa
            });
        }

        public static IActionResult CaptchaResponse()
        {
            return new OkObjectResult(new
            {
                enable_captcha = true,
                message = "Captcha enabled. Please verify."
            });
        }

    }
}