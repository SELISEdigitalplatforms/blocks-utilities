using Blocks.Genesis;

namespace DomainService.People
{
    public class SignupRequest
    {
        public string Email { get; set; }
        public string? CaptchaCode { get; set; }
    }

    public class SignupResponse : BaseResponse
    {
        public string? ItemId { get; set; }
    }
}
