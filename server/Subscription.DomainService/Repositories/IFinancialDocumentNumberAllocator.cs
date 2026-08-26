using Subscription.DomainService.Enums;

namespace Subscription.DomainService.Repositories;

/// <summary>
/// Hands out the next document number for a tenant and a year.
/// </summary>
/// <remarks>
/// Its own collaborator rather than a method on the document repository, because it is the one part of
/// issuing a document that mutates shared state before the document exists. Keeping it separate makes
/// the ordering explicit at the call site: allocate, then insert, and if the insert loses a duplicate
/// race the number is abandoned rather than reused.
/// <para>
/// Numbers may therefore have gaps. That is the correct trade: a gap is a question an auditor can
/// answer from the ledger, while a reused number is two different documents claiming to be the same
/// one, which nothing can answer.
/// </para>
/// </remarks>
public interface IFinancialDocumentNumberAllocator
{
    /// <param name="year">
    /// The issue year, taken from the document's own issue date rather than from the clock. A payment
    /// settled on 31 December and documented on 1 January belongs to December's sequence.
    /// </param>
    Task<string> AllocateAsync(
        string tenantId,
        FinancialDocumentType documentType,
        int year,
        CancellationToken cancellationToken);
}
