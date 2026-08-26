using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Scheduling;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// That every part of the document machinery is actually registered.
/// </summary>
/// <remarks>
/// All of it is resolved by the container inside a worker, where there is no request to fail visibly.
/// A forgotten registration therefore does not surface as a broken endpoint — it surfaces as documents
/// quietly never being issued, which is the one failure this feature exists to prevent.
/// <para>
/// Descriptors rather than resolution, deliberately. Resolving would need the payment module and the
/// platform's own services registered too, which is a test about the host's composition and is covered
/// there; the defect this guards against is a service the module forgot to add at all.
/// </para>
/// </remarks>
public sealed class SubscriptionDocumentRegistrationTests
{
    private static readonly Type[] DocumentServices =
    [
        typeof(ISubscriptionFinancialDocumentIssuer),
        typeof(ISubscriptionFinancialDocumentAnnouncer),
        typeof(ISubscriptionFinancialDocumentDeliveryService),
        typeof(ISubscriptionFinancialDocumentHistoryService),
        typeof(ISubscriptionBillingProfileService),
        typeof(ISubscriptionBillingProfileGuard),
        typeof(ISubscriptionMerchantProfileService),
        typeof(ISubscriptionFinancialDocumentRepository),
        typeof(ISubscriptionBillingProfileRepository),
        typeof(ISubscriptionMerchantProfileRepository),
        typeof(ISubscriptionDocumentCursorRepository),
        typeof(IFinancialDocumentNumberAllocator),
        typeof(IFinancialDocumentPdfRenderer),
        typeof(IFinancialDocumentFileStore),
    ];

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_document_service_is_registered(Type serviceType) =>
        Registered().Should().Contain(serviceType);

    [Fact]
    public void Both_document_work_handlers_are_registered_for_the_dispatcher_to_find()
    {
        var handlers = Build()
            .Where(descriptor => descriptor.ServiceType == typeof(ISubscriptionWorkHandler))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        // Resolved by work type at dispatch, from whatever is registered under this one interface. A
        // handler nobody added leaves its work sitting in the queue until it is dead-lettered, with
        // nothing to say why.
        handlers.Should().Contain(typeof(FinancialDocumentIssueWorkHandler));
        handlers.Should().Contain(typeof(FinancialDocumentDeliveryWorkHandler));
    }

    [Fact]
    public void The_document_work_types_keep_their_numbers()
    {
        // Persisted on queued work, so renumbering would have the dispatcher hand a delivery item to
        // the issuer. Asserted here beside the registrations because the two are what connect a
        // stored number to the code that runs for it.
        ((int)SubscriptionWorkType.FinancialDocumentIssue).Should().Be(7);
        ((int)SubscriptionWorkType.FinancialDocumentDelivery).Should().Be(8);
    }

    public static TheoryData<Type> Services()
    {
        var data = new TheoryData<Type>();

        foreach (var serviceType in DocumentServices)
        {
            data.Add(serviceType);
        }

        return data;
    }

    private static HashSet<Type> Registered() =>
        [.. Build().Select(descriptor => descriptor.ServiceType)];

    private static IServiceCollection Build()
    {
        var services = new ServiceCollection();

        services.RegisterSubscriptionDomainServices(
            new ConfigurationBuilder().AddInMemoryCollection([]).Build());

        return services;
    }
}
