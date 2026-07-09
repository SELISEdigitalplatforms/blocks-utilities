namespace Mail.DomainService.Mails.Services.DeliveryTracking;

public interface IAmazonSnsMessageVerifier
{
    Task<bool> VerifyAsync(string payloadJson, CancellationToken cancellationToken = default);
}
