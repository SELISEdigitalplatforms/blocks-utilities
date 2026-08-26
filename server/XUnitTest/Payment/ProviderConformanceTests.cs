
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;

namespace XUnitTest.Payment;

/// <summary>
/// Holds every provider to the same contract, so a provider that is missing a capability fails
/// here rather than in production against that provider only.
/// </summary>
/// <remarks>
/// Payment code is full of shared services that quietly encode one provider's conventions.
/// Per-provider unit tests do not catch that, because each fixture shares the assumption it is
/// testing. These tests assert across providers instead: what every provider must implement,
/// and that nothing implemented was left unregistered.
/// </remarks>
public sealed class ProviderConformanceTests
{
    /// <summary>
    /// Abstractions no provider may go without. A provider missing one of these cannot complete
    /// a payment at all, so the gap must surface at build time rather than at first use.
    /// </summary>
    private static readonly Type[] RequiredOfEveryProvider =
    [
        typeof(IProviderInitiationRequestFactory),
        typeof(IProviderEndpointPolicy),
        typeof(IProviderSecretHydrator),
        typeof(IProviderCredentialRotationStrategy),
        typeof(IWebhookNormalizer),
        typeof(IWebhookSignatureVerifier),
        typeof(IPaymentRefundProviderGateway),
        typeof(IPaymentCaptureProviderGateway),
        typeof(IStoredPaymentMethodProviderGateway),
        typeof(IStoredPaymentChargeProviderGateway),
        typeof(IPaymentSessionClient),
        typeof(ICheckoutResultClient),
        typeof(ICheckoutStatusMapper)
    ];

    /// <summary>
    /// Abstractions a provider may legitimately not implement, each with the reason it is
    /// optional. Anything not listed here and not in
    /// <see cref="RequiredOfEveryProvider"/> is an oversight, not a decision.
    /// </summary>
    private static readonly Dictionary<Type, string> OptionalByDesign = new()
    {
        [typeof(IStoredPaymentMethodDetailProviderGateway)] =
            "Adyen reports card brand and last four on the webhook that stores the card; " +
            "Stripe does not, so only Stripe needs a read-back.",

        [typeof(IPaymentMethodSetupRequestFactory)] =
            "Collecting a card without charging it needs a provider operation for exactly that. " +
            "Stripe has one; a provider without it can still take every payment, and asking it " +
            "for a zero-amount charge instead is the thing this capability exists to avoid. " +
            "Callers are told the provider cannot do it rather than being given a broken session."
    };

    private static readonly IPaymentProviderCatalog Catalog = new PaymentProviderCatalog();

    public static TheoryData<string, Type> RequiredCapabilities()
    {
        var data = new TheoryData<string, Type>();

        foreach (var providerName in Catalog.RegisteredProviderNames)
        {
            foreach (var capability in RequiredOfEveryProvider)
            {
                data.Add(providerName, capability);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RequiredCapabilities))]
    public void Every_registered_provider_implements_every_required_capability(
        string providerName,
        Type capability)
    {
        var implementations = ImplementationsOf(capability)
            .Where(implementation => Supports(implementation, providerName))
            .ToList();

        implementations.Should().NotBeEmpty(
            "{0} must implement {1} or it cannot complete a payment",
            providerName,
            capability.Name);
    }

    /// <summary>
    /// Two implementations claiming the same provider is worse than none: the resolver takes
    /// whichever the container happens to return first, so the behaviour is arbitrary and
    /// changes with registration order.
    /// </summary>
    [Theory]
    [MemberData(nameof(RequiredCapabilities))]
    public void No_capability_has_two_implementations_for_one_provider(
        string providerName,
        Type capability)
    {
        var implementations = ImplementationsOf(capability)
            .Where(implementation => Supports(implementation, providerName))
            .ToList();

        implementations.Should().HaveCount(1);
    }

    /// <summary>
    /// A gateway that exists but was never registered resolves to null, and the operation
    /// reports the provider as unsupported — indistinguishable from not having written it.
    /// </summary>
    [Fact]
    public void Every_provider_capability_implementation_is_registered_for_injection()
    {
        var services = new ServiceCollection();
        services.RegisterPaymentDomainServices(
            new ConfigurationBuilder().Build());

        var unregistered = new List<string>();

        foreach (var capability in RequiredOfEveryProvider.Concat(OptionalByDesign.Keys))
        {
            var implementations = ImplementationsOf(capability).ToList();
            var descriptors = services
                .Where(descriptor => descriptor.ServiceType == capability)
                .ToList();

            // Most capabilities name their implementation directly. Endpoint policies are
            // registered twice — once as themselves so they can be injected concretely, once
            // as a factory aliasing the interface — so those descriptors carry no
            // implementation type and can only be counted.
            var named = descriptors
                .Where(descriptor => descriptor.ImplementationType != null)
                .Select(descriptor => descriptor.ImplementationType!)
                .ToHashSet();

            unregistered.AddRange(
                implementations
                    .Where(implementation => !named.Contains(implementation))
                    .Where(_ => descriptors.Count < implementations.Count)
                    .Select(implementation =>
                        $"{implementation.Name} as {capability.Name}"));
        }

        unregistered.Should().BeEmpty(
            "an implementation that is never registered resolves to null, and the operation " +
            "reports the provider as unsupported — indistinguishable from not having written it");
    }

    /// <summary>
    /// Guards the list above from going stale: a capability that is neither required nor
    /// explicitly optional has never been considered, and a provider silently lacking it is
    /// exactly the failure this class exists to prevent.
    /// </summary>
    [Fact]
    public void Every_provider_scoped_capability_is_classified()
    {
        var classified = RequiredOfEveryProvider
            .Concat(OptionalByDesign.Keys)
            .ToHashSet();

        var discovered = typeof(IPaymentProviderCatalog).Assembly
            .GetTypes()
            .Where(type =>
                type is { IsInterface: true, IsPublic: true } &&
                type.GetMethod("Supports", [typeof(string)]) != null)
            .ToList();

        discovered.Should().NotBeEmpty();
        discovered.Should().OnlyContain(
            type => classified.Contains(type),
            "a provider-scoped capability must be listed as required of every provider or " +
            "documented as optional, so nobody adds one and leaves a provider behind");
    }

    private static IEnumerable<Type> ImplementationsOf(Type capability) =>
        capability.Assembly
            .GetTypes()
            .Where(type =>
                type is { IsClass: true, IsAbstract: false } &&
                capability.IsAssignableFrom(type));

    /// <summary>
    /// Asks the implementation itself, rather than matching on type names. Naming is a
    /// convention that drifts; <c>Supports</c> is what the resolver actually calls.
    /// </summary>
    private static bool Supports(Type implementation, string providerName)
    {
        var supports = implementation.GetMethod("Supports", [typeof(string)]);

        if (supports == null) return false;

        // Every provider capability is stateless in its Supports decision, so an uninitialised
        // instance answers correctly without the dependencies the constructor asks for.
        var instance = System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(implementation);

        return (bool)supports.Invoke(instance, [providerName])!;
    }

}
