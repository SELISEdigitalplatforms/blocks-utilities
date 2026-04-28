namespace Iam.DomainService.Activities
{
    public interface IUserActivityService
    {
        Task<GetSessionsResponse> GetSessionsAsync(BaseActivityRequest request);
        Task<GetHistorysResponse> GetHistoriesAsync(BaseActivityRequest request);
    }
}
