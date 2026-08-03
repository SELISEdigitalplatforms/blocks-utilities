using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Payment.DomainService.Providers;
using Payment.DomainService.Providers.HostedCheckout;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using DomainService.Utilities;
using Mail.DomainService.Shared.Utilities;

namespace XUnitTest.Integration;

/// <summary>
/// The composition root is the only place that says which implementation the
/// running service actually gets, and a wrong lifetime or a missing
/// registration only shows up at runtime. These tests pin the shape of the
/// container rather than the presence of a line of code.
/// </summary>
public sealed class ServiceRegistrationTests
{
    private static IConfiguration Configuration(
        Dictionary<string, string?>? overrides = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(overrides ?? [])
            .Build();

    private static IServiceCollection PaymentServices(
        IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.RegisterPaymentDomainServices(
            configuration ?? Configuration());

        return services;
    }

    private static ServiceDescriptor Descriptor<TService>(
        IServiceCollection services) =>
        services.Single(descriptor =>
            descriptor.ServiceType == typeof(TService));

    private static IReadOnlyList<Type> ImplementationsOf<TService>(
        IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(TService))
            .Select(descriptor => descriptor.ImplementationType!)
            .ToList();

    [Fact]
    public void The_payment_registration_returns_the_same_collection_for_chaining()
    {
        var services = new ServiceCollection();

        var returned = services.RegisterPaymentDomainServices(Configuration());

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void Payment_options_are_bound_from_the_payment_section()
    {
        var services = PaymentServices(Configuration(new Dictionary<string, string?>
        {
            ["Payment:ProviderTimeoutSeconds"] = "42",
            ["Payment:PaymentQueryTenantRequestsPerMinute"] = "7"
        }));

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<PaymentOptions>>()
            .Value;

        options.ProviderTimeoutSeconds.Should().Be(42);
        options.PaymentQueryTenantRequestsPerMinute.Should().Be(7);
    }

    [Fact]
    public void Payment_options_fall_back_to_their_defaults_when_unconfigured()
    {
        var options = PaymentServices().BuildServiceProvider()
            .GetRequiredService<IOptions<PaymentOptions>>()
            .Value;

        options.ProviderTimeoutSeconds.Should().Be(15);
        options.ProviderCacheSeconds.Should().Be(120);
    }

    [Theory]
    [InlineData(typeof(IPaymentRepository), typeof(PaymentRepository))]
    [InlineData(typeof(IPaymentQueryRepository), typeof(PaymentQueryRepository))]
    [InlineData(typeof(IPaymentRefundRepository), typeof(PaymentRefundRepository))]
    [InlineData(typeof(IPaymentCaptureRepository), typeof(PaymentCaptureRepository))]
    [InlineData(typeof(IPaymentWebhookInboxRepository), typeof(PaymentWebhookInboxRepository))]
    [InlineData(typeof(IStoredPaymentMethodRepository), typeof(StoredPaymentMethodRepository))]
    public void Every_payment_repository_is_registered_as_a_singleton(
        Type serviceType,
        Type implementationType)
    {
        var descriptor = PaymentServices().Single(candidate =>
            candidate.ServiceType == serviceType);

        descriptor.ImplementationType.Should().Be(implementationType);
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Request_scoped_services_are_scoped_not_singletons()
    {
        var services = PaymentServices();

        Descriptor<IPaymentService>(services).Lifetime
            .Should().Be(ServiceLifetime.Scoped);
        Descriptor<IPaymentQueryService>(services).Lifetime
            .Should().Be(ServiceLifetime.Scoped);
        Descriptor<IPaymentWebhookIntakeService>(services).Lifetime
            .Should().Be(ServiceLifetime.Scoped);
        Descriptor<IPaymentProviderCredentialRotationService>(services).Lifetime
            .Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Validators_are_transient_so_state_never_leaks_between_requests()
    {
        var services = PaymentServices();

        Descriptor<IValidator<MakePaymentRequest>>(services).Lifetime
            .Should().Be(ServiceLifetime.Transient);
        Descriptor<IValidator<GetPaymentsRequest>>(services).Lifetime
            .Should().Be(ServiceLifetime.Transient);
        Descriptor<IValidator<RotatePaymentProviderCredentialsRequest>>(services)
            .Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void Both_providers_are_registered_for_every_pluggable_boundary()
    {
        var services = PaymentServices();

        ImplementationsOf<IPaymentSessionClient>(services)
            .Should().Contain([
                typeof(HostedCheckoutSessionClient),
                typeof(StripeCheckoutSessionClient)
            ]);
        ImplementationsOf<ICheckoutResultClient>(services)
            .Should().Contain([
                typeof(HostedCheckoutResultClient),
                typeof(StripeCheckoutResultClient)
            ]);
        ImplementationsOf<IProviderSecretHydrator>(services)
            .Should().HaveCount(2);
        ImplementationsOf<IProviderCredentialRotationStrategy>(services)
            .Should().HaveCount(2);
        ImplementationsOf<IWebhookSignatureVerifier>(services)
            .Should().HaveCount(2);
        ImplementationsOf<IWebhookNormalizer>(services)
            .Should().HaveCount(2);
    }

    [Fact]
    public void The_endpoint_policies_are_shared_instances_not_second_copies()
    {
        // Both the concrete type and the interface must resolve to one object,
        // otherwise the allow-list a policy caches is built twice.
        var services = PaymentServices();

        var policyDescriptors = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(IProviderEndpointPolicy))
            .ToList();

        policyDescriptors.Should().HaveCount(2);
        policyDescriptors.Should().OnlyContain(descriptor =>
            descriptor.ImplementationFactory != null);
    }

    [Fact]
    public void The_startup_tasks_are_registered_as_hosted_services()
    {
        var hostedServices = ImplementationsOf<IHostedService>(PaymentServices());

        hostedServices.Should().Contain(typeof(ProviderSecretMigrationStartupTask));
        hostedServices.Should().Contain(typeof(PaymentConfigurationReadinessLogger));
    }

    [Fact]
    public void Notification_services_register_every_receiver_strategy()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.RegisterAllNotificationApplicationServices();

        services.Should().Contain(descriptor =>
            descriptor.ServiceType ==
            typeof(DomainService.Notification.FilterSpecificReceiver));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType ==
            typeof(DomainService.Notification.UserSpecificReceiver));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType ==
            typeof(DomainService.Notification.SignalRNotificationServiceProvider));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType ==
            typeof(DomainService.Notification.FirebaseNotificationServiceProvider));
    }

    [Fact]
    public void Mail_services_register_both_smtp_clients_as_transient()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.RegisterAllMailApplicationServices();

        services.Single(descriptor =>
                descriptor.ServiceType ==
                typeof(global::Mail.DomainService.Mails.MailKitSmtpClient))
            .Lifetime.Should().Be(ServiceLifetime.Transient);
        services.Single(descriptor =>
                descriptor.ServiceType ==
                typeof(global::Mail.DomainService.Mails.MicrosoftSmtpClient))
            .Lifetime.Should().Be(ServiceLifetime.Transient);
        services.Single(descriptor =>
                descriptor.ServiceType ==
                typeof(global::Mail.DomainService.Mails.SmtpClientProvider))
            .Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void Utility_services_register_the_shared_http_helper()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.RegisterUtilityServices();

        services.Should().Contain(descriptor =>
            descriptor.ServiceType ==
            typeof(Utility.DomainService.Shared.Services.IHttpHelperServices));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType ==
            typeof(Utility.DomainService.Sequence.service.ISequenceService));
    }
}
