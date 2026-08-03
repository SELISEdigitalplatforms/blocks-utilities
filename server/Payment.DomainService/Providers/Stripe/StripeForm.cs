using System.Globalization;

namespace Payment.DomainService.Providers.Stripe;

/// <summary>
/// Builds Stripe's <c>application/x-www-form-urlencoded</c> request bodies.
/// </summary>
/// <remarks>
/// Stripe expresses nesting through bracketed keys rather than JSON, so
/// <c>line_items[0][price_data][currency]=usd</c> is one field, not a structure. This builder
/// composes those keys explicitly: it is a flat dictionary with a naming convention, which
/// keeps the wire format visible at the call site instead of hidden behind reflection.
/// Null values are omitted, because Stripe treats a present-but-empty field as an
/// instruction to clear it.
/// </remarks>
public sealed class StripeForm
{
    private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);
    private readonly string _prefix;

    public StripeForm()
        : this(string.Empty)
    {
    }

    private StripeForm(string prefix)
    {
        _prefix = prefix;
    }

    /// <summary>The accumulated fields, ready to send.</summary>
    public Dictionary<string, string> Fields => _fields;

    public StripeForm Add(string name, string? value)
    {
        if (value != null)
        {
            _fields[Key(name)] = value;
        }

        return this;
    }

    public StripeForm Add(string name, long? value) =>
        Add(name, value?.ToString(CultureInfo.InvariantCulture));

    public StripeForm Add(string name, int? value) =>
        Add(name, value?.ToString(CultureInfo.InvariantCulture));

    public StripeForm Add(string name, bool? value) =>
        Add(name, value.HasValue ? value.Value ? "true" : "false" : null);

    /// <summary>Adds a nested object, as <c>name[child]=…</c>.</summary>
    public StripeForm AddObject(string name, Action<StripeForm> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var nested = new StripeForm(Key(name));
        build(nested);

        foreach (var (key, value) in nested.Fields)
        {
            _fields[key] = value;
        }

        return this;
    }

    /// <summary>Adds one element of an array, as <c>name[index][child]=…</c>.</summary>
    public StripeForm AddArrayItem(string name, int index, Action<StripeForm> build) =>
        AddObject(
            string.Create(CultureInfo.InvariantCulture, $"{name}[{index}]"),
            build);

    /// <summary>
    /// Adds a metadata map. Entries with a null value are skipped rather than sent empty.
    /// </summary>
    public StripeForm AddMetadata(IEnumerable<KeyValuePair<string, string?>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return AddObject("metadata", metadata =>
        {
            foreach (var (key, value) in entries)
            {
                metadata.Add(key, value);
            }
        });
    }

    private string Key(string name) =>
        _prefix.Length == 0 ? name : $"{_prefix}[{name}]";
}
