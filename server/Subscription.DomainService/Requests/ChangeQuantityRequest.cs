namespace Subscription.DomainService.Requests;

/// <summary>
/// A new purchased quantity for one of a subscription's items.
/// </summary>
/// <remarks>
/// The same body serves the preview and the update, so what a caller is quoted is what it then
/// applies — a preview that took a different shape could be answered from different inputs.
/// </remarks>
public sealed class ChangeQuantityRequest
{
    /// <summary>
    /// The version the caller last read. Required: without it a stale administrator's tab can
    /// overwrite a seat count somebody else changed a minute ago.
    /// </summary>
    public int Version { get; set; }

    public List<QuantityChangeItemRequest> Quantities { get; set; } = [];

    /// <summary>
    /// Which organization this subscription belongs to. Ignored unless the caller is the console,
    /// the same rule every other subscription endpoint follows.
    /// </summary>
    public string? OrganizationId { get; set; }
}

public sealed class QuantityChangeItemRequest
{
    public string ItemKey { get; set; } = string.Empty;

    public long Quantity { get; set; }
}
