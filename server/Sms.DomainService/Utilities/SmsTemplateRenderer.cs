using System.Text.RegularExpressions;

namespace Sms.DomainService.Utilities;

/// <summary>
/// <c>{{key}}</c> placeholders, whitespace inside the braces allowed, keys matched case-insensitively.
/// </summary>
public static partial class SmsTemplateRenderer
{
    /// <summary>Distinct placeholder keys in order of first appearance.</summary>
    public static IReadOnlyList<string> Placeholders(string body) =>
        PlaceholderPattern().Matches(body)
            .Select(m => m.Groups["key"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Renders the body. Keys the data does not supply are returned in <paramref name="missing"/>
    /// and left in place, so a caller can refuse to send text with raw placeholders in it.
    /// </summary>
    public static string Render(string body, IReadOnlyDictionary<string, string> data, out IReadOnlyList<string> missing)
    {
        var values = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
        var absent = new List<string>();

        var rendered = PlaceholderPattern().Replace(body, match =>
        {
            var key = match.Groups["key"].Value;
            if (values.TryGetValue(key, out var value))
            {
                return value;
            }

            if (!absent.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                absent.Add(key);
            }

            return match.Value;
        });

        missing = absent;
        return rendered;
    }

    [GeneratedRegex(@"\{\{\s*(?<key>[A-Za-z0-9_.\-]+)\s*\}\}")]
    private static partial Regex PlaceholderPattern();
}
