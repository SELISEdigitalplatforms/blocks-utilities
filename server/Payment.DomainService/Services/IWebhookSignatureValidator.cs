using System.Security.Cryptography;
using System.Text;
using Payment.DomainService.Models.HostedCheckout;

namespace Payment.DomainService.Services;

public interface IWebhookSignatureValidator
{
    bool ValidateStandard(NotificationItem item, string activeKey, string? previousKey);
    bool ValidateToken(string rawBody, string suppliedSignature, string activeKey, string? previousKey);
}
