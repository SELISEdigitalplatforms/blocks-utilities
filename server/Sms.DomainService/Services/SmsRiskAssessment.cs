using Sms.DomainService.Enums;

namespace Sms.DomainService.Services;

public class SmsRiskAssessment
{
    public SmsRiskLevel RiskLevel { get; set; } = SmsRiskLevel.Low;
    public List<string> Reasons { get; set; } = [];
    public bool ShouldBlock => RiskLevel == SmsRiskLevel.Blocked;
}
