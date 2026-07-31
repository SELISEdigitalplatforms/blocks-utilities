using FluentAssertions;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Enums;
using Payment.DomainService.Repositories;
using Payment.DomainService.Responses;
using Payment.DomainService.Services;

namespace XUnitTest.Payment;

/// <summary>
/// The single-payment endpoint applies the same organization scope as the listing.
/// </summary>
/// <remarks>
/// Filtering only the list would be theatre: anyone holding an identifier could still read the
/// payment directly, and identifiers travel in URLs, logs and support tickets.
/// </remarks>
public sealed class PaymentServiceOrganizationVisibilityTests
{
    [Fact]
    public async Task An_organization_may_read_its_own_payment()
    {
        var result = await GetAsync(
            callerOrganization: "organization-1",
            paymentOrganization: "organization-1");

        result.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Reported as not found rather than forbidden, so the response cannot be used to confirm
    /// that an identifier exists in another organization.
    /// </summary>
    [Fact]
    public async Task Another_organizations_payment_is_reported_as_not_found()
    {
        var result = await GetAsync(
            callerOrganization: "organization-1",
            paymentOrganization: "organization-2");

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.NotFound);
        result.ErrorCode.Should().Be("payment_not_found");
    }

    /// <summary>
    /// Payments made before organizations existed belong to none and are the tenant's shared
    /// history, so they stay readable rather than vanishing on the day a tenant is split.
    /// </summary>
    [Fact]
    public async Task A_payment_predating_organizations_stays_readable()
    {
        var result = await GetAsync(
            callerOrganization: "organization-1",
            paymentOrganization: null);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_caller_without_an_organization_may_read_any_of_the_tenants_payments()
    {
        var result = await GetAsync(
            callerOrganization: null,
            paymentOrganization: "organization-2");

        result.IsSuccess.Should().BeTrue();
    }

    private static async Task<PaymentOperationResult> GetAsync(
        string? callerOrganization,
        string? paymentOrganization)
    {
        var contextResolver = new Mock<IPaymentExecutionContextResolver>();
        contextResolver.Setup(resolver => resolver.Resolve(It.IsAny<string>()))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(
                    "tenant-1",
                    "actor-1",
                    callerOrganization),
                null));

        var repository = new Mock<IPaymentRepository>();
        repository.Setup(item => item.GetByIdAsync(
                "tenant-1", "payment-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentDetail
            {
                ItemId = "payment-1",
                TenantId = "tenant-1",
                OrganizationId = paymentOrganization
            });

        var responseMapper = new Mock<IPaymentResponseMapper>();
        responseMapper.Setup(mapper => mapper.Map(It.IsAny<PaymentDetail>()))
            .Returns(new PaymentResponse { PaymentDetailId = "payment-1" });

        var service = new PaymentService(
            contextResolver.Object,
            Mock.Of<IPaymentPreflightService>(),
            Mock.Of<IPaymentDistributedLock>(),
            Mock.Of<IPaymentReservationService>(),
            Mock.Of<IPaymentInitiationService>(),
            repository.Object,
            responseMapper.Object,
            Mock.Of<IRecurringPaymentInitiationService>());

        return await service.GetPaymentAsync(
            "payment-1",
            "corr-1",
            CancellationToken.None);
    }
}
