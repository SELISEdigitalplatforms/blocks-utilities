namespace Payment.DomainService.Utilities;

public static class PaymentLogValue
{
    private const int HashLength = 16;
    private const int MaximumLabelLength = 64;
    private const int MaximumIdentifierLength = 128;

    /// <summary>
    /// A system identifier — payment id, order id, merchant id, correlation id — written in
    /// clear so it can be searched for.
    /// </summary>
    /// <remarks>
    /// These name records, not people, and hashing them was the reason the logs could not be
    /// followed: an operator holding a payment id from the database or the console had no way to
    /// reach its log lines without recomputing the digest by hand. Personal data — the shopper's
    /// email, name and phone — still goes through <see cref="Hash"/>.
    /// <para>
    /// Sanitised, not passed through: order and merchant identifiers come from the caller, and a
    /// newline in one would let a request forge whole log entries.
    /// </para>
    /// </remarks>
    public static string Id(string? value) =>
        Sanitize(value, MaximumIdentifierLength);

    public static string Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        var hash = PaymentHashing.HashSensitiveValue(value);

        return hash[..Math.Min(HashLength, hash.Length)];
    }

    public static string Label(string? value) =>
        Sanitize(value, MaximumLabelLength);

    private static string Sanitize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        var safeCharacters = value
            .Where(character =>
                char.IsLetterOrDigit(character) ||
                character is '_' or '-' or '.')
            .Take(maximumLength)
            .ToArray();

        return safeCharacters.Length == 0
            ? "invalid"
            : new string(safeCharacters);
    }
}
