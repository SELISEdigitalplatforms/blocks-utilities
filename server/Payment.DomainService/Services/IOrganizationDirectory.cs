namespace Payment.DomainService.Services;

/// <summary>
/// Answers whether an organization exists for the calling tenant.
/// </summary>
/// <remarks>
/// Provider registration is the one write that accepts an organization from the request body
/// rather than the caller's context, so that a console whose context is always the default
/// organization can still configure the others. That is only safe if the value is checked
/// against the directory of record before anything is written under it.
/// </remarks>
public interface IOrganizationDirectory
{
    Task<OrganizationLookupOutcome> FindAsync(
        string organizationId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Three states, not a bool. "We could not ask" must never be mistaken for "it does not
/// exist": the first has to fail closed with a retryable error, and the second is the
/// caller's mistake to correct.
/// </summary>
public enum OrganizationLookupOutcome
{
    Found = 0,
    NotFound = 1,
    Unavailable = 2
}
