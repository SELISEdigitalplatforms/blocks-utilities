using System.Text.RegularExpressions;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;

namespace Sms.DomainService.Services;

public class SuspiciousMessageService : ISuspiciousMessageService
{
    private static readonly Regex UrlRegex = new(@"https?://|www\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SmsRiskAssessment Analyze(string messageText, IReadOnlyCollection<string> destinationNumbers, SmsSpamFilterSettings settings)
    {
        var result = new SmsRiskAssessment();
        if (!settings.Enabled)
        {
            return result;
        }

        if (destinationNumbers.Count > settings.MaxRecipients)
        {
            Raise(result, SmsRiskLevel.Blocked, $"Recipient count exceeds the configured maximum of {settings.MaxRecipients}.");
        }

        if (messageText.Length > settings.MaxMessageLength)
        {
            Raise(result, SmsRiskLevel.Medium, "Message body is longer than the configured maximum.");
        }

        if (!UrlRegex.IsMatch(messageText))
        {
            return result;
        }

        if (settings.BlockedTerms.Any(term => messageText.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            Raise(result, SmsRiskLevel.Blocked, "Message combines a blocked term with a URL.");
        }
        else if (settings.UrlPolicy == SmsUrlPolicy.Block)
        {
            Raise(result, SmsRiskLevel.Blocked, "Messages containing URLs are not allowed.");
        }
        else if (settings.UrlPolicy == SmsUrlPolicy.Flag)
        {
            Raise(result, SmsRiskLevel.High, "Message contains a URL.");
        }

        return result;
    }

    private static void Raise(SmsRiskAssessment result, SmsRiskLevel level, string reason)
    {
        if (level > result.RiskLevel)
        {
            result.RiskLevel = level;
        }

        result.Reasons.Add(reason);
    }
}
