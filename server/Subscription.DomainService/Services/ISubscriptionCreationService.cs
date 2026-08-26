using Subscription.DomainService.Entities;
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
}
