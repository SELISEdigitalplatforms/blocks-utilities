using System.Text.RegularExpressions;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Services;

public class SuspiciousMessageService : ISuspiciousMessageService
{
    private static readonly Regex UrlRegex = new(@"https?://|www\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] BlockedTerms = ["password", "otp", "bank", "wallet", "crypto"];

    public SmsRiskAssessment Analyze(string messageText, IReadOnlyCollection<string> destinationNumbers)
    {
        var result = new SmsRiskAssessment();

        if (destinationNumbers.Count > 100)
        {
            result.RiskLevel = SmsRiskLevel.Blocked;
            result.Reasons.Add("Recipient fanout exceeds the allowed safety threshold.");
        }

        if (messageText.Length > 1000)
        {
            result.RiskLevel = Max(result.RiskLevel, SmsRiskLevel.Medium);
            result.Reasons.Add("Message body is unusually long.");
        }

        var hasUrl = UrlRegex.IsMatch(messageText);
        var hasSensitiveTerm = BlockedTerms.Any(term => messageText.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (hasUrl && hasSensitiveTerm)
        {
            result.RiskLevel = SmsRiskLevel.Blocked;
            result.Reasons.Add("Message combines sensitive terms with a URL.");
        }
        else if (hasUrl)
        {
            result.RiskLevel = Max(result.RiskLevel, SmsRiskLevel.High);
            result.Reasons.Add("Message contains a URL.");
        }

        return result;
    }

    private static SmsRiskLevel Max(SmsRiskLevel current, SmsRiskLevel candidate)
    {
        return candidate > current ? candidate : current;
    }
}
