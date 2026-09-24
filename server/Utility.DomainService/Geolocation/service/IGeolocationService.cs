namespace Utility.DomainService.Geolocation.service
{
    public interface IGeolocationService
    {
        Task<LocateIpResponse> LocateIpAsync(
            LocateIpRequest request,
            CancellationToken cancellationToken = default);
    }
}
