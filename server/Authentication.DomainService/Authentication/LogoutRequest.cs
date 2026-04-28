namespace DomainService.Authentication
{
    public class LogoutRequest
    {
        public string RefreshToken { get; set; }
    }

    public class LogoutResponse
    {
        public bool IsSuccess { get; set; }
    }
}
