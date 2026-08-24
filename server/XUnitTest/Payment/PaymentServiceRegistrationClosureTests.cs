using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.DomainService.Scheduling;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Every payment service's dependencies are registered, so nothing fails on first use.
/// </summary>
/// <remarks>
/// A missing registration is invisible until something asks for the service: the container builds
/// happily, the process starts, and the first request that reaches the service returns a 500 naming
/// a type nobody has heard of. That is how <c>IPaymentWorkDispatcher</c> came to be unregistered on
/// <c>dev</c> — a rename rewrote the line that registered it instead of adding one beside it, and
/// eight services depended on it.
/// <para>
/// This walks constructors rather than resolving anything, so it needs no database, no message bus
/// and no fakes for the infrastructure the host supplies. What it checks is closure: if a registered
/// implementation asks for a payment interface, that interface has to be registered too.
/// </para>
/// <para>
/// Deliberately here rather than beside <c>ServiceRegistrationTests</c>. That file lives under
/// <c>Integration</c>, and the ordinary test run filters that folder out — which is the other reason
/// this defect reached a running environment.
/// </para>
/// </remarks>
public sealed class PaymentServiceRegistrationClosureTests
{
    [Fact]
    public void Every_registered_payment_service_can_have_its_dependencies_resolved()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterPaymentDomainServices(
            new ConfigurationBuilder().AddInMemoryCollection([]).Build());

        var registered = services
            .Select(descriptor => descriptor.ServiceType)
            .ToHashSet();

        var missing = new List<string>();

        foreach (var descriptor in services)
        {
            var implementation = descriptor.ImplementationType;

            // Factories and pre-built instances decide their own dependencies, and a factory's body
            // is not something reflection can read. Those are what the resolving tests next door are
            // for.
            if (implementation is null || implementation.Assembly != typeof(PaymentOptions).Assembly)
            {
                continue;
            }

            foreach (var constructor in implementation.GetConstructors())
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    var dependency = parameter.ParameterType;

                    // Only this module's own interfaces. Everything else — IMessageClient,
                    // IBlocksSecret, IOptions, ILogger — comes from the host or the framework, and
                    // asserting on those here would be asserting on somebody else's container.
                    if (!dependency.IsInterface ||
                        dependency.Assembly != typeof(PaymentOptions).Assembly ||
                        registered.Contains(dependency))
                    {
                        continue;
                    }

                    // Enumerables are satisfied by zero or more registrations, so an unregistered
                    // element type is a legitimate empty collection rather than a failure.
                    if (dependency.IsGenericType &&
                        dependency.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        continue;
                    }

                    missing.Add($"{implementation.Name} needs {dependency.Name}");
                }
            }
        }

        // Named individually, because the useful failure message is which service needs what — not
        // that some number of them are broken.
        missing.Should().BeEmpty();
    }

    [Fact]
    public void The_two_payment_work_dispatchers_are_both_registered()
    {
        // Two different things whose names differ by one word. IPaymentWorkDispatcher sends a work
        // *command* onto the bus and is what webhook intake, state transitions, refunds, captures
        // and the outboxes depend on; IPaymentBackgroundWorkDispatcher drains the durable queue.
        // Losing either to the other's name is the mistake this pins down.
        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterPaymentDomainServices(
            new ConfigurationBuilder().AddInMemoryCollection([]).Build());

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IPaymentWorkDispatcher) &&
            descriptor.ImplementationType == typeof(PaymentWorkDispatcher));

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IPaymentBackgroundWorkDispatcher) &&
            descriptor.ImplementationType == typeof(PaymentBackgroundWorkDispatcher));
    }
}
