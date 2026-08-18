using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Providers.Stripe;
using Payment.DomainService.Repositories;
using Payment.DomainService.Services;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Services;

namespace XUnitTest.Subscription;

/// <summary>Serving a subscriber the invoice behind one of their subscription payments.</summary>
public sealed class SubscriptionInvoiceDocumentServiceTests
{
    private const string TenantId = "tenant-1";
    private const string CallerOrganizationId = "org-subscriber";
    private const string MerchantOrganizationId = "default";
    private const string PaymentId = "pay-1";

    private readonly Mock<ISubscriptionContextResolver> _context = new();
    private readonly Mock<IPaymentRepository> _payments = new();
    private readonly Mock<IPaymentProviderCache> _providers = new();
    private readonly Mock<IStripeInvoiceClient> _invoices = new();

    public SubscriptionInvoiceDocumentServiceTests()
    {
        _context
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Resolved(
                new SubscriptionContext(TenantId, CallerOrganizationId, "actor-1", "user-1")));

        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPayment());

        _providers
            .Setup(cache => cache.GetAsync(
                TenantId,
                MerchantOrganizationId,
                PaymentConstants.StripeProvider,
                It.IsAny<Func<Task<PaymentProvider?>>>()))
            .ReturnsAsync(new PaymentProvider
            {
                ProviderName = PaymentConstants.StripeProvider,
                IsEnabled = true,
                ApiKey = "sk_test_1",
                ApiBaseUrl = StripeConstants.ApiBaseUrl
            });

        _invoices
            .Setup(client => client.DownloadInvoicePdfAsync(
                It.IsAny<PaymentProvider>(), "in_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceDocument([1, 2, 3], "application/pdf", "TVUYQTSF-0002"));
    }

    [Fact]
    public async Task The_owning_organization_gets_the_document()
    {
        var result = await Service().GetAsync(PaymentId, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Content.Should().Equal([1, 2, 3]);
        result.Value.ContentType.Should().Be("application/pdf");
        result.Value.FileName.Should().Be("invoice-TVUYQTSF-0002.pdf");
    }

    [Fact]
    public async Task The_provider_is_resolved_at_the_merchant_scope_that_took_the_money()
    {
        await Service().GetAsync(PaymentId, null, "corr-1", CancellationToken.None);

        // The subscriber's own organization has no provider configured — resolving there would
        // make every invoice undownloadable.
        _providers.Verify(
            cache => cache.GetAsync(
                TenantId,
                MerchantOrganizationId,
                PaymentConstants.StripeProvider,
                It.IsAny<Func<Task<PaymentProvider?>>>()),
            Times.Once);
    }

    [Fact]
    public async Task Another_organizations_invoice_is_not_found()
    {
        var payment = NewPayment();
        payment.CustomerOrganizationId = "org-somebody-else";
        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var result = await Service().GetAsync(PaymentId, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        await NothingWasDownloaded();
    }

    [Fact]
    public async Task A_payment_with_no_subscriber_recorded_is_not_handed_out()
    {
        // Payments taken before the subscriber was captured. An unattributed billing document is
        // the last thing to serve on a guess.
        var payment = NewPayment();
        payment.CustomerOrganizationId = null;
        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var result = await Service().GetAsync(PaymentId, null, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        await NothingWasDownloaded();
    }

    [Fact]
    public async Task A_payment_that_is_not_a_subscription_invoice_is_not_found()
    {
        var payment = NewPayment();
        payment.PaymentFlow = PaymentFlows.HostedCheckout;
        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var result = await Service().GetAsync(PaymentId, null, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        await NothingWasDownloaded();
    }

    [Fact]
    public async Task A_missing_payment_is_not_found()
    {
        _payments
            .Setup(repository => repository.GetByIdAsync(
                TenantId, PaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentDetail?)null);

        var result = await Service().GetAsync(PaymentId, null, "corr-1", CancellationToken.None);

        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should().Be("subscription_invoice_not_found");
    }

    [Fact]
    public async Task An_unresolved_caller_never_reaches_the_payment()
    {
        _context
            .Setup(resolver => resolver.ResolveAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SubscriptionContextResolution.Unresolved(
                PaymentFailureKind.Validation,
                "subscription_organization_missing",
                "No organization."));

        var result = await Service().GetAsync(PaymentId, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("subscription_organization_missing");
        _payments.Verify(
            repository => repository.GetByIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task A_document_the_provider_will_not_give_up_is_reported_as_unavailable()
    {
        _invoices
            .Setup(client => client.DownloadInvoicePdfAsync(
                It.IsAny<PaymentProvider>(), "in_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StripeInvoiceDocument?)null);

        var result = await Service().GetAsync(PaymentId, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("subscription_invoice_document_unavailable");
    }

    [Fact]
    public async Task An_invoice_number_cannot_shape_the_download_filename()
    {
        // The number reaches a Content-Disposition header, so it is provider-supplied text in a
        // response header until it is stripped.
        _invoices
            .Setup(client => client.DownloadInvoicePdfAsync(
                It.IsAny<PaymentProvider>(), "in_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeInvoiceDocument(
                [1], "application/pdf", "../../etc/passwd\"; x=\"y"));

        var result = await Service().GetAsync(PaymentId, null, "corr-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FileName.Should().Be("invoice-etcpasswdxy.pdf");
    }

    private async Task NothingWasDownloaded() =>
        await Task.Run(() => _invoices.Verify(
            client => client.DownloadInvoicePdfAsync(
                It.IsAny<PaymentProvider>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never));

    private static PaymentDetail NewPayment() => new()
    {
        ItemId = PaymentId,
        TenantId = TenantId,
        ProviderName = PaymentConstants.StripeProvider,
        PaymentFlow = PaymentFlows.SubscriptionInvoice,
        PaymentStatus = PaymentStatuses.Captured,
        OrganizationId = MerchantOrganizationId,
        CustomerOrganizationId = CallerOrganizationId,
        ProviderInvoiceId = "in_1"
    };

    private SubscriptionInvoiceDocumentService Service() => new(
        _context.Object,
        _payments.Object,
        _providers.Object,
        _invoices.Object,
        NullLogger<SubscriptionInvoiceDocumentService>.Instance);
}
