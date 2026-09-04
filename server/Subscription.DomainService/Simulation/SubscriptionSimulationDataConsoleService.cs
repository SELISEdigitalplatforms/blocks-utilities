using System.Globalization;
using Blocks.Genesis;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Payment.DomainService.Enums;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Responses;
using Subscription.DomainService.Services;

namespace Subscription.DomainService.Simulation;

public sealed class SubscriptionSimulationDataConsoleService : ISubscriptionSimulationDataConsoleService
{
    private const int MaximumFindLimit = 100;

    private readonly ISubscriptionContextResolver _contextResolver;
    private readonly IDbContextProvider _db;
    private readonly ISubscriptionSimulationRunRepository _simulationRuns;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptions;

    public SubscriptionSimulationDataConsoleService(
        ISubscriptionContextResolver contextResolver,
        IDbContextProvider db,
        ISubscriptionSimulationRunRepository simulationRuns,
        IOptionsMonitor<PaymentOptions> paymentOptions)
    {
        _contextResolver = contextResolver;
        _db = db;
        _simulationRuns = simulationRuns;
        _paymentOptions = paymentOptions;
    }

    public async Task<SubscriptionOperationResult<SubscriptionSimulationDataQueryResponse>> FindAsync(
        string logicalCollection,
        FindDataRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policyResult = ResolvePolicy<SubscriptionSimulationDataQueryResponse>(
            logicalCollection, requireRead: true, correlationId);

        if (policyResult.Failure is { } policyFailure)
        {
            return policyFailure;
        }

        var policy = policyResult.Policy!;

        if (request.Limit is <= 0 or > MaximumFindLimit)
        {
            return SubscriptionOperationResult<SubscriptionSimulationDataQueryResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_simulation_limit_invalid",
                $"limit must be between 1 and {MaximumFindLimit}.",
                correlationId);
        }

        var (context, failure) = await ResolveAsync<SubscriptionSimulationDataQueryResponse>(
            request.OrganizationId, correlationId, cancellationToken);

        if (failure is not null || context is null)
        {
            return failure!;
        }

        var collection = _db.GetDatabase(context.TenantId).GetCollection<BsonDocument>(policy.CollectionName);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("TenantId", context.TenantId),
            Builders<BsonDocument>.Filter.Eq("OrganizationId", context.OrganizationId),
            Builders<BsonDocument>.Filter.Eq(policy.SubscriptionIdField, request.SubscriptionId));

        var documents = await collection.Find(filter).Limit(request.Limit).ToListAsync(cancellationToken);

        await RecordRunAsync(
            context, request.SubscriptionId, "DataConsoleFind", correlationId,
            $"collection={policy.LogicalName}", cancellationToken);

        return SubscriptionOperationResult<SubscriptionSimulationDataQueryResponse>.Success(
            new SubscriptionSimulationDataQueryResponse
            {
                Collection = policy.LogicalName,
                Count = documents.Count,
                Documents = documents
                    .Select(document => Redact(policy.LogicalName, document).ToJson())
                    .ToList(),
                CorrelationId = correlationId
            },
            correlationId);
    }

    public async Task<SubscriptionOperationResult<SubscriptionSimulationDataMutationResponse>> UpdateFieldsAsync(
        string logicalCollection,
        UpdateDataFieldRequest request,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policyResult = ResolvePolicy<SubscriptionSimulationDataMutationResponse>(
            logicalCollection, requireRead: false, correlationId);

        if (policyResult.Failure is { } policyFailure)
        {
            return policyFailure;
        }

        var policy = policyResult.Policy!;

        if (request.Fields.Count == 0)
        {
            return SubscriptionOperationResult<SubscriptionSimulationDataMutationResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_simulation_no_fields",
                "At least one field must be given.",
                correlationId);
        }

        var disallowed = request.Fields.Keys
            .Where(field => !policy.UpdatableFields.Contains(field, StringComparer.Ordinal))
            .ToList();

        if (disallowed.Count > 0)
        {
            return SubscriptionOperationResult<SubscriptionSimulationDataMutationResponse>.Failure(
                PaymentFailureKind.Validation,
                "subscription_simulation_field_not_allowed",
                $"{policy.LogicalName} does not allow writing: {string.Join(", ", disallowed)}.",
                correlationId,
                new Dictionary<string, string[]> { ["Fields"] = disallowed.ToArray() });
        }

        var updates = new List<UpdateDefinition<BsonDocument>>();

        foreach (var field in request.Fields)
        {
            if (!DateTime.TryParse(
                    field.Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return SubscriptionOperationResult<SubscriptionSimulationDataMutationResponse>.Failure(
                    PaymentFailureKind.Validation,
                    "subscription_simulation_field_value_invalid",
                    $"'{field.Key}' must be an ISO 8601 UTC timestamp.",
                    correlationId);
            }

            updates.Add(Builders<BsonDocument>.Update.Set(field.Key, parsed));
        }

        var (context, failure) = await ResolveAsync<SubscriptionSimulationDataMutationResponse>(
            request.OrganizationId, correlationId, cancellationToken);

        if (failure is not null || context is null)
        {
            return failure!;
        }

        var collection = _db.GetDatabase(context.TenantId).GetCollection<BsonDocument>(policy.CollectionName);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("TenantId", context.TenantId),
            Builders<BsonDocument>.Filter.Eq("OrganizationId", context.OrganizationId),
            Builders<BsonDocument>.Filter.Eq(policy.SubscriptionIdField, request.SubscriptionId));

        var result = await collection.UpdateOneAsync(
            filter, Builders<BsonDocument>.Update.Combine(updates), cancellationToken: cancellationToken);

        await RecordRunAsync(
            context, request.SubscriptionId, "DataConsoleUpdate", correlationId,
            $"collection={policy.LogicalName};fields={string.Join(',', request.Fields.Keys)}",
            cancellationToken);

        return SubscriptionOperationResult<SubscriptionSimulationDataMutationResponse>.Success(
            new SubscriptionSimulationDataMutationResponse
            {
                Collection = policy.LogicalName,
                Modified = result.ModifiedCount > 0,
                FieldsSet = request.Fields.Keys.ToList(),
                CorrelationId = correlationId
            },
            correlationId);
    }

    private static (SimulationCollectionPolicy? Policy, SubscriptionOperationResult<T>? Failure) ResolvePolicy<T>(
        string logicalCollection, bool requireRead, string correlationId)
    {
        var policy = SubscriptionSimulationDataConsolePolicy.Find(logicalCollection);

        if (policy is null)
        {
            return (null, SubscriptionOperationResult<T>.Failure(
                PaymentFailureKind.NotFound,
                "subscription_simulation_collection_not_allowed",
                $"'{logicalCollection}' is not an allowlisted collection.",
                correlationId));
        }

        if (requireRead && !policy.CanRead)
        {
            return (null, SubscriptionOperationResult<T>.Failure(
                PaymentFailureKind.Validation,
                "subscription_simulation_collection_not_readable",
                $"'{logicalCollection}' does not allow reads.",
                correlationId));
        }

        return (policy, null);
    }

    private async Task<(SubscriptionContext? Context, SubscriptionOperationResult<T>? Failure)> ResolveAsync<T>(
        string? organizationId, string correlationId, CancellationToken cancellationToken)
    {
        var caller = BlocksContext.GetContext();

        if (!SubscriptionSimulationGuard.IsAuthorized(
                caller?.OrganizationId, _paymentOptions.CurrentValue, caller?.Permissions))
        {
            return (null, SubscriptionOperationResult<T>.Failure(
                PaymentFailureKind.Unavailable,
                "subscription_simulation_forbidden",
                "This caller may not use the subscription simulation harness.",
                correlationId));
        }

        // A blank organization is left to the resolver, which reads it as the console's own
        // organization for a console caller — the same answer POST /subscriptions gives. See
        // SubscriptionSimulationService.ResolveTargetAsync for the whole reasoning.
        var resolution = await _contextResolver.ResolveAsync(correlationId, organizationId, cancellationToken);

        if (!resolution.IsSuccess || resolution.Context is null)
        {
            return (null, resolution.ToFailure<T>(correlationId));
        }

        return (resolution.Context, null);
    }

    private Task RecordRunAsync(
        SubscriptionContext context,
        string subscriptionId,
        string action,
        string correlationId,
        string requestSummary,
        CancellationToken cancellationToken) =>
        _simulationRuns.AppendAsync(
            new Entities.SubscriptionSimulationRun
            {
                TenantId = context.TenantId,
                OrganizationId = context.OrganizationId,
                SubscriptionId = subscriptionId,
                ActorId = context.ActorId,
                Action = action,
                RequestSummary = requestSummary,
                CorrelationId = correlationId,
                Outcome = "Succeeded",
                CompletedAtUtc = DateTime.UtcNow
            },
            cancellationToken);

    /// <summary>
    /// Never a stored payment method id, a provider customer id or a provider organization id —
    /// the same fields <see cref="SubscriptionSimulationService"/>'s state read already excludes.
    /// </summary>
    private static BsonDocument Redact(string logicalName, BsonDocument document)
    {
        if (logicalName != "subscriptions")
        {
            return document;
        }

        if (document.TryGetValue("SettlementReservation", out var reservation) &&
            reservation is BsonDocument reservationDocument)
        {
            reservationDocument.Remove("StoredPaymentMethodId");
            reservationDocument.Remove("ProviderCustomerId");
            reservationDocument.Remove("ProviderOrganizationId");
        }

        return document;
    }
}
