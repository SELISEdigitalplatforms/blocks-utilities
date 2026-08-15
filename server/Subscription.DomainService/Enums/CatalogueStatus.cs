namespace Subscription.DomainService.Enums;

/// <summary>
/// Whether a plan or price may be sold. Retiring one never removes it: existing subscribers
/// hold a snapshot and keep running on terms that are no longer on the menu.
/// </summary>
public enum CatalogueStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}
