using DomainService.OAuth.RequestModel;
using Microsoft.AspNetCore.Mvc;

namespace DomainService.OAuth
{
    public interface IOAuthTokenProvider
    {
        Task<IActionResult> AuthenticateAsync(TokenRequest request);
        Task<GetSocialLogInEndPointResponse> GetSocialLogInEndPointAsync(GetSocialLogInEndPointRequest request);
    }
}
