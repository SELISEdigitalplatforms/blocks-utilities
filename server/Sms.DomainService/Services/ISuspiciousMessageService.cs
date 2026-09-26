using Sms.DomainService.Entities;

namespace Sms.DomainService.Services;

public interface ISuspiciousMessageService
{
    SmsRiskAssessment Analyze(string messageText, IReadOnlyCollection<string> destinationNumbers, SmsSpamFilterSettings settings);
}
