namespace Sms.DomainService.Utilities;

public static class SmsLogSanitizer
{
    private const int MaximumIdentifierLength = 128;

    /// <summary>
    /// An identifier for a log line: tenant, message, correlation or provider error id. Written in
    /// clear so it can be searched for, but sanitized, because several arrive from outside (the
    /// request body, the webhook route, a provider's response) and a line break in one would let a
    /// caller forge whole log entries.
    /// </summary>
    public static string Id(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        // Line breaks go first, as their own step, so nothing after this can reintroduce a new entry.
        var withoutLineBreaks = value.Replace("\r", string.Empty).Replace("\n", string.Empty);
        var safe = withoutLineBreaks
            .Where(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.')
            .Take(MaximumIdentifierLength)
            .ToArray();

        return safe.Length == 0 ? "invalid" : new string(safe);
    }

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
