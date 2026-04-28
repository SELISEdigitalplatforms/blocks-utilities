namespace Iam.DomainService.Accounts
{
    public class BaseAccountResponse
    {
        public bool IsSuccess { get; set; }
        public Dictionary<string, string> Errors { get; set; }
    }
}
