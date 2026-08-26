using Blocks.Genesis;
using MongoDB.Driver;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;

namespace Payment.DomainService.Repositories;

public sealed class PaymentQueryRepository :
    IPaymentQueryRepository
{
    private readonly IDbContextProvider _dbContextProvider;
    private readonly IPaymentRepository _paymentRepository;

    public PaymentQueryRepository(
        IDbContextProvider dbContextProvider,
        IPaymentRepository paymentRepository)
    {
        _dbContextProvider = dbContextProvider;
        _paymentRepository = paymentRepository;
    }

    public async Task<PaymentQueryPage> QueryAsync(
        PaymentQueryCriteria criteria,
        CancellationToken cancellationToken)
    {
        await _paymentRepository.EnsureIndexesAsync(
            criteria.TenantId,
            cancellationToken);

        var collection = Collection(criteria.TenantId);
        var filter = BuildQueryFilter(criteria);
        var sort = BuildSort(criteria);
        var records = await collection
            .Find(filter)
            .Sort(sort)
            .Limit(criteria.PageSize + 1)
            .Project(payment => new PaymentQueryRecord
            {
                PaymentDetailId = payment.ItemId,
                ProviderName = payment.ProviderName,
                Amount = payment.PreciseAmount,
                CurrencyCode = payment.CurrencyCode,
                PaymentDateUtc = payment.PaymentDate,
                PaymentStatus = payment.PaymentStatus,
                PaymentFlow = payment.PaymentFlow,
                HasInvoice = payment.ProviderInvoiceId != null && payment.ProviderInvoiceId != "",
                HasPendingRefund =
                    (payment.Refunds ??
                     new List<PaymentRefund>())
                    .Any(
                        refund =>
                            refund.Status ==
                            PaymentRefundStatuses.Initiating ||
                            refund.Status ==
                            PaymentRefundStatuses
                                .InitiationUnknown ||
                            refund.Status ==
                            PaymentRefundStatuses.Submitted ||
                            refund.Status ==
                            PaymentRefundStatuses
                                .RequiresAttention)
            })
            .ToListAsync(cancellationToken);

        var hasMore = records.Count > criteria.PageSize;

        if (hasMore)
        {
            records.RemoveAt(records.Count - 1);
        }

        if (criteria.IsBackward)
        {
            records.Reverse();
        }

        return new PaymentQueryPage(records, hasMore);
    }

    /// <summary>
    /// The filter behind <see cref="QueryAsync"/>, public so the organization scope can be
    /// asserted without a database. A wrong scope still returns plausible payments, just the
    /// wrong set of them, which no result inspection would reveal.
    /// </summary>
    public static FilterDefinition<PaymentDetail> BuildQueryFilter(
        PaymentQueryCriteria criteria)
    {
        var filters = new List<FilterDefinition<PaymentDetail>>
        {
            Builders<PaymentDetail>.Filter.Eq(
                payment => payment.TenantId,
                criteria.TenantId)
        };

        // A named organization replaces the caller's own scope rather than narrowing within
        // it, so the request decides what comes back. That is what makes this usable by an
        // integration acting for several organizations, and it is also why any authenticated
        // caller in the tenant can read any organization's payments by naming one. Decided
        // deliberately; see PaymentQueryCriteria.RequestedOrganizationId.
        if (!string.IsNullOrWhiteSpace(criteria.RequestedOrganizationId))
        {
            filters.Add(Builders<PaymentDetail>.Filter.Or(
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.OrganizationId, criteria.RequestedOrganizationId),
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.CustomerOrganizationId, criteria.RequestedOrganizationId)));
        }

        // Otherwise an organization sees its own payments and the ones made before
        // organizations existed, which belong to no organization and are the tenant's shared
        // history. Excluding those would empty every console the day a tenant is split. A
        // caller with no organization is not scoped at all and sees the whole tenant, which is
        // how every tenant behaved before this filter and how one that never uses
        // organizations still behaves.
        else if (!string.IsNullOrWhiteSpace(criteria.OrganizationId))
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Or(
                    Builders<PaymentDetail>.Filter.Eq(
                        payment => payment.OrganizationId,
                        criteria.OrganizationId),
                    Builders<PaymentDetail>.Filter.Eq(
                        payment => payment.CustomerOrganizationId,
                        criteria.OrganizationId),
                    Builders<PaymentDetail>.Filter.Eq(
                        payment => payment.OrganizationId,
                        null)));
        }

        if (criteria.ProviderNames.Length > 0)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.In(
                    payment => payment.ProviderName,
                    criteria.ProviderNames));
        }

        if (criteria.PaymentStatuses.Length > 0)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.In(
                    payment => payment.PaymentStatus,
                    criteria.PaymentStatuses));
        }

        if (criteria.MinAmount.HasValue)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Gte(
                    payment => payment.PreciseAmount,
                    criteria.MinAmount.Value));
        }

        if (criteria.MaxAmount.HasValue)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Lte(
                    payment => payment.PreciseAmount,
                    criteria.MaxAmount.Value));
        }

        if (criteria.PaymentDateFromUtc.HasValue)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Gte(
                    payment => payment.PaymentDate,
                    criteria.PaymentDateFromUtc.Value));
        }

        if (criteria.PaymentDateToUtc.HasValue)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Lt(
                    payment => payment.PaymentDate,
                    criteria.PaymentDateToUtc.Value));
        }

        AddOptionalEqualityFilters(filters, criteria);

        // Card setups are not payments. They carry a zero amount, settle at Authorized and never
        // capture, so listing them puts rows in a payment history that no one can reconcile and
        // that every total has to remember to skip. Excluded here rather than at each caller, so
        // a new report cannot forget.
        filters.Add(
            Builders<PaymentDetail>.Filter.Ne(
                payment => payment.PaymentFlow,
                PaymentFlows.PaymentMethodSetup));

        if (criteria.CursorBoundary != null)
        {
            filters.Add(BuildCursorFilter(criteria));
        }

        return Builders<PaymentDetail>.Filter.And(filters);
    }

    private static void AddOptionalEqualityFilters(
        ICollection<FilterDefinition<PaymentDetail>> filters,
        PaymentQueryCriteria criteria)
    {
        if (criteria.CurrencyCode != null)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.CurrencyCode,
                    criteria.CurrencyCode));
        }

        if (criteria.OrderId != null)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.OrderId,
                    criteria.OrderId));
        }

        if (criteria.PaymentDetailId != null)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.ItemId,
                    criteria.PaymentDetailId));
        }

        if (criteria.PaymentFlow != null)
        {
            filters.Add(
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.PaymentFlow,
                    criteria.PaymentFlow));
        }
    }

    private static FilterDefinition<PaymentDetail> BuildCursorFilter(
        PaymentQueryCriteria criteria)
    {
        var boundary = criteria.CursorBoundary!;
        var requestedAscending =
            criteria.SortDirection ==
            PaymentQuerySortDirections.Ascending;
        var useGreaterThan =
            requestedAscending != criteria.IsBackward;

        var fieldComparison = BuildFieldComparison(
            criteria.SortBy,
            boundary,
            useGreaterThan);
        var fieldEquality = BuildFieldEquality(
            criteria.SortBy,
            boundary);
        var idComparison = useGreaterThan
            ? Builders<PaymentDetail>.Filter.Gt(
                payment => payment.ItemId,
                boundary.PaymentDetailId)
            : Builders<PaymentDetail>.Filter.Lt(
                payment => payment.ItemId,
                boundary.PaymentDetailId);

        return Builders<PaymentDetail>.Filter.Or(
            fieldComparison,
            Builders<PaymentDetail>.Filter.And(
                fieldEquality,
                idComparison));
    }

    private static FilterDefinition<PaymentDetail> BuildFieldComparison(
        string sortBy,
        PaymentQueryCursorBoundary boundary,
        bool useGreaterThan) =>
        sortBy switch
        {
            PaymentQuerySortFields.ProviderName => useGreaterThan
                ? Builders<PaymentDetail>.Filter.Gt(
                    payment => payment.ProviderName,
                    boundary.TextValue!)
                : Builders<PaymentDetail>.Filter.Lt(
                    payment => payment.ProviderName,
                    boundary.TextValue!),
            PaymentQuerySortFields.Amount => useGreaterThan
                ? Builders<PaymentDetail>.Filter.Gt(
                    payment => payment.PreciseAmount,
                    boundary.AmountValue!.Value)
                : Builders<PaymentDetail>.Filter.Lt(
                    payment => payment.PreciseAmount,
                    boundary.AmountValue!.Value),
            PaymentQuerySortFields.PaymentDate => useGreaterThan
                ? Builders<PaymentDetail>.Filter.Gt(
                    payment => payment.PaymentDate,
                    boundary.PaymentDateUtc!.Value)
                : Builders<PaymentDetail>.Filter.Lt(
                    payment => payment.PaymentDate,
                    boundary.PaymentDateUtc!.Value),
            PaymentQuerySortFields.PaymentStatus => useGreaterThan
                ? Builders<PaymentDetail>.Filter.Gt(
                    payment => payment.PaymentStatus,
                    boundary.TextValue!)
                : Builders<PaymentDetail>.Filter.Lt(
                    payment => payment.PaymentStatus,
                    boundary.TextValue!),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortBy),
                sortBy,
                "Unsupported payment sort field.")
        };

    private static FilterDefinition<PaymentDetail> BuildFieldEquality(
        string sortBy,
        PaymentQueryCursorBoundary boundary) =>
        sortBy switch
        {
            PaymentQuerySortFields.ProviderName =>
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.ProviderName,
                    boundary.TextValue!),
            PaymentQuerySortFields.Amount =>
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.PreciseAmount,
                    boundary.AmountValue!.Value),
            PaymentQuerySortFields.PaymentDate =>
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.PaymentDate,
                    boundary.PaymentDateUtc!.Value),
            PaymentQuerySortFields.PaymentStatus =>
                Builders<PaymentDetail>.Filter.Eq(
                    payment => payment.PaymentStatus,
                    boundary.TextValue!),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortBy),
                sortBy,
                "Unsupported payment sort field.")
        };

    private static SortDefinition<PaymentDetail> BuildSort(
        PaymentQueryCriteria criteria)
    {
        var requestedAscending =
            criteria.SortDirection ==
            PaymentQuerySortDirections.Ascending;
        var queryAscending = criteria.IsBackward
            ? !requestedAscending
            : requestedAscending;
        var sortBuilder = Builders<PaymentDetail>.Sort;
        var fieldSort = criteria.SortBy switch
        {
            PaymentQuerySortFields.ProviderName => queryAscending
                ? sortBuilder.Ascending(payment => payment.ProviderName)
                : sortBuilder.Descending(payment => payment.ProviderName),
            PaymentQuerySortFields.Amount => queryAscending
                ? sortBuilder.Ascending(payment => payment.PreciseAmount)
                : sortBuilder.Descending(payment => payment.PreciseAmount),
            PaymentQuerySortFields.PaymentDate => queryAscending
                ? sortBuilder.Ascending(payment => payment.PaymentDate)
                : sortBuilder.Descending(payment => payment.PaymentDate),
            PaymentQuerySortFields.PaymentStatus => queryAscending
                ? sortBuilder.Ascending(payment => payment.PaymentStatus)
                : sortBuilder.Descending(payment => payment.PaymentStatus),
            _ => throw new ArgumentOutOfRangeException(
                nameof(criteria.SortBy),
                criteria.SortBy,
                "Unsupported payment sort field.")
        };
        var idSort = queryAscending
            ? sortBuilder.Ascending(payment => payment.ItemId)
            : sortBuilder.Descending(payment => payment.ItemId);

        return sortBuilder.Combine(fieldSort, idSort);
    }

    private IMongoCollection<PaymentDetail> Collection(
        string tenantId) =>
        _dbContextProvider
            .GetDatabase(RequireTenant(tenantId))
            .GetCollection<PaymentDetail>("PaymentDetails");

    private static string RequireTenant(string tenantId) =>
        string.IsNullOrWhiteSpace(tenantId)
            ? throw new InvalidOperationException(
                "Tenant context is required for payment queries.")
            : tenantId;
}
