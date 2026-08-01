namespace Payment.DomainService.Repositories;

/// <summary>
/// Identifies whose cards to list, and at which organization's merchant account.
/// </summary>
/// <param name="ShopperReference">
/// The reference derived under one provider's own key, so a card is only findable under the
/// reference of the provider that stored it.
/// </param>
/// <param name="OrganizationId">
/// The organization that saved the card, which is the one the paying caller belonged to — the
/// same value the card is stamped with when it is stored. Null is the tenant-level scope, where
/// every card saved before organizations existed lives.
/// </param>
/// <remarks>
/// Deliberately not the resolved configuration's organization. Those differ whenever an
/// organization has no configuration of its own and falls back to the tenant's, and reading by
/// the configuration while writing by the caller made every such card invisible the moment it
/// was saved. Charging still resolves the configuration from the same caller organization the
/// card is stamped with, so a listed card is always one its merchant account can charge.
/// </remarks>
/// <remarks>
/// Pairing the two is what makes the filter safe. The shopper reference alone looks like it
/// separates organizations — it is an HMAC under each organization's own key — but that only
/// holds while those keys differ. Registration deliberately accepts an existing shopper key so
/// a migration does not orphan saved cards, and supplying one key to two organizations makes
/// their references collide. Then the card is offered at the wrong merchant account, which
/// cannot charge it, and the shopper sees a decline on a card that looks perfectly good.
/// </remarks>
public sealed record StoredPaymentMethodLookupScope(
    string ShopperReference,
    string? OrganizationId);
