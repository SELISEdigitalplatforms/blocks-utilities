using System.Text.RegularExpressions;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// The language a notification email is asked for in, as the mail module matches it.
/// </summary>
/// <remarks>
/// The mail module selects a template by an exact match on this value and has no fallback, so a
/// stored language that no template uses means that recipient silently gets nothing. Checked for
/// BCP-47 shape only (<c>en</c>, <c>en-US</c>, <c>de-CH</c>), not against a culture list: an
/// ICU-less container image would refuse valid tags, and which languages exist is the tenant's
/// templates' business, not this service's.
/// </remarks>
public static partial class MailLanguage
{
    public const int MaximumLength = 35;

    /// <summary>Judged as it will be stored: trimmed, the way every other contact field is.</summary>
    public static bool IsWellFormed(string? value) =>
        value?.Trim() is { Length: > 0 and <= MaximumLength } trimmed && Shape().IsMatch(trimmed);

    /// <summary>The first usable value, else <see cref="SubscriptionConstants.DefaultMailLanguage"/>.</summary>
    public static string FirstOf(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim()
        ?? SubscriptionConstants.DefaultMailLanguage;

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
