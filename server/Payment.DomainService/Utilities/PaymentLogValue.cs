namespace Payment.DomainService.Utilities;

public static class PaymentLogValue
{
    private const int HashLength = 16;
    private const int MaximumLabelLength = 64;

    public static string Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        var hash = PaymentHashing.HashSensitiveValue(value);

        return hash[..Math.Min(HashLength, hash.Length)];
    }

    public static string Label(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "missing";
        }

        var safeCharacters = value
            .Where(character =>
                char.IsLetterOrDigit(character) ||
                character is '_' or '-' or '.')
            .Take(MaximumLabelLength)
            .ToArray();

        return safeCharacters.Length == 0
            ? "invalid"
            : new string(safeCharacters);
    }
}
