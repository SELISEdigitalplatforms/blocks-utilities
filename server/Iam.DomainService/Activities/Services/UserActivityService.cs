using Microsoft.Extensions.Logging;

namespace Iam.DomainService.Activities
{
    public class UserActivityService : IUserActivityService
    {
        private readonly IUserActivityRepository _userActivityRepository;

        public UserActivityService(
            IUserActivityRepository userActivityRepository
        )
        {
            _userActivityRepository = userActivityRepository;
        }

        public async Task<GetHistorysResponse> GetHistoriesAsync(BaseActivityRequest request)
        {
            var (historys, count) = await _userActivityRepository.GetHistorysByUserIdDevicesAsync(request);

            return new GetHistorysResponse
            {
                Data = historys,
                TotalCount = count
            };
        }

        public async Task<GetSessionsResponse> GetSessionsAsync(BaseActivityRequest request)
        {
            var (sessions, count) = await _userActivityRepository.GetActiveSessionByUserIdDevicesAsync(request);

            return new GetSessionsResponse
            {
                Data = sessions,
                TotalCount = count
            };
        }
    }
}
