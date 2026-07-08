namespace Sms.DomainService.Utilities;

public static class SmsLogSanitizer
{
    public static string MaskPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return string.Empty;
        }

        var trimmed = phoneNumber.Trim();
        if (trimmed.Length <= 4)
        {
            return "****";
        }

        return new string('*', Math.Max(0, trimmed.Length - 4)) + trimmed[^4..];
    }

    public static string SanitizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        return message.Length <= 300 ? message : message[..300];
    }
}
