namespace DomainService.Shared.Services
{
    public interface IEncodingService
    {
        Task<string> EncodeToBase26Async(string input, string tenantGroupId, int length);
    }
}