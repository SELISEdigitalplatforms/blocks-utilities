using System.Security.Cryptography;
using System.Text;

namespace Payment.DomainService.Utilities;

/// <summary>
/// Turns provider-supplied identifiers into safe, stable, collision-free identifier fragments.
/// </summary>
/// <remarks>
/// Merchant identifiers carry characters that are unsafe in identifiers — a Stripe account id
/// looks like <c>acct_1ABC</c>. Stripping them alone is not enough: <c>acct_1A</c> and
/// <c>acct-1A</c> would strip to the same text and silently share whatever the slug names. A
/// short hash of the original value is appended so distinct inputs always produce distinct
/// slugs, while the readable part still tells an operator which merchant they are looking at.
/// </remarks>
internal static class PaymentSlug
{
    private const int MaximumReadableLength = 40;
    private const int HashCharacters = 8;

    public static string Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var readable = new StringBuilder(
            Math.Min(value.Length, MaximumReadableLength));

        foreach (var character in value)
        {
            if (readable.Length == MaximumReadableLength) break;

            if (char.IsAsciiLetterOrDigit(character) || character == '-')
            {
                readable.Append(character);
            }
        }

        return readable.Length == 0
            ? Fingerprint(value)
            : $"{readable}-{Fingerprint(value)}";
    }

    private static string Fingerprint(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);

        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes))
                .ToLowerInvariant()[..HashCharacters];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
