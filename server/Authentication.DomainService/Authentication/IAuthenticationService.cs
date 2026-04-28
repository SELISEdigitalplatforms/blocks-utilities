using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Shared.RequestModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DomainService.Authentication
{
    public interface IAuthenticationService
    {
        Task<LogoutResponse> LogoutUser(string refreshToken, HttpRequest httpRequest);
        string CookieToken(HttpRequest request);
        bool DeleteCookie(HttpRequest request);
        Task<IActionResult> GetLoginOptionsAsync();
        Task<OIDCClientCredential> GetClientCredentialAsync(string clientId);
        Task<ClaimsPrincipal?> GetPrincipalFromTokenAsync(HttpRequest request, string tenantId, bool IsUserInfoGetRequest = false);
        Task<string> ConstructRedirectUriAsync(string clientId, AcknowledgeRequest request);
    }
}
