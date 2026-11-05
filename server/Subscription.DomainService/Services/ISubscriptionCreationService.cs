using Subscription.DomainService.Entities;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Requests;
using Subscription.DomainService.Responses;

namespace Subscription.DomainService.Services;

public interface ISubscriptionCreationService
{
    /// <summary>
    /// Builds and stores a subscription in its initial state.
    /// </summary>
    /// <remarks>
    /// Stops short of taking money. Whether a charge is needed at all — and how it is raised —
    /// belongs to the checkout service, so this one stays about turning a chosen plan into a
    /// durable record.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionDetail>> CreateAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Builds and stores an offline subscription in its initial state. See spec 006.
    /// </summary>
    /// <remarks>
    /// The same build as <see cref="CreateAsync"/>, so the terms, the schedule and the frozen opening
    /// figure are decided by the code that decides them for a card subscriber — with three
    /// differences. No payment provider has to be ready, because none is ever called. The billing
    /// profile is always required, because every offline document is addressed to it. And a
    /// <see cref="OfflinePricingMode.Contract"/> amount replaces the catalogue figure before the
    /// opening charge is frozen, so everything downstream reads the contract without knowing it is one.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionDetail>> CreateOfflineAsync(
        CreateSubscriptionRequest request,
        OfflinePricing pricing,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// What <see cref="CreateAsync"/> would charge and when it would renew, without storing
    /// anything.
    /// </summary>
    /// <remarks>
    /// Runs the same resolution and the same arithmetic as <see cref="CreateAsync"/>, stopped
    /// short of every write — so a caller that confirms what it was quoted gets what it was
    /// quoted. A condition that would refuse the confirm (an existing subscription, an
    /// incomplete billing profile) is reported as a blocker here rather than as a failure, so
    /// the price and the obstacle are seen together.
    /// </remarks>
    Task<SubscriptionOperationResult<SubscriptionPreviewResponse>> PreviewAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken);

    Task<SubscriptionOperationResult<SubscriptionDiscountPreviewResponse>> PreviewDiscountAsync(
        CreateSubscriptionRequest request,
        SubscriptionContext context,
        string correlationId,
        CancellationToken cancellationToken);
}
