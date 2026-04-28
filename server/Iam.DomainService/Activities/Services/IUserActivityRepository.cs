namespace Iam.DomainService.Activities
{
    public interface IUserActivityRepository
    {
        Task<(IQueryable<object>, long)> GetActiveSessionByUserIdDevicesAsync(BaseActivityRequest request);
        Task<(IQueryable<object>, long)> GetHistorysByUserIdDevicesAsync(BaseActivityRequest request);
    }
}
