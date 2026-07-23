using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.DomainService.Enums;
using Payment.DomainService.Models;
using Payment.DomainService.Repositories;
using Payment.DomainService.Requests;
using Payment.DomainService.Services;
using Payment.DomainService.Validators;

namespace XUnitTest.Payment;

public sealed class PaymentQueryServiceTests
{
    [Fact]
    public async Task Query_uses_resolved_tenant_and_normalized_filters()
    {
        var repository = new Mock<IPaymentQueryRepository>();
        PaymentQueryCriteria? captured = null;
        repository.Setup(x => x.QueryAsync(
                It.IsAny<PaymentQueryCriteria>(),
                It.IsAny<CancellationToken>()))
            .Callback<PaymentQueryCriteria, CancellationToken>(
                (criteria, _) => captured = criteria)
            .ReturnsAsync(new PaymentQueryPage([], false));
        var rateLimiter = AllowedRateLimiter();
        var service = Service(repository, rateLimiter);

        var result = await service.GetPaymentsAsync(
            new GetPaymentsRequest
            {
                ProviderNames = [" adyen-online ", "ADYEN-ONLINE"],
                PaymentStatuses = ["authorized"],
                CurrencyCode = "chf",
                PaymentFlow = "hosted_checkout",
                PaymentDateFromUtc = new DateTimeOffset(
                    2026,
                    7,
                    1,
                    6,
                    0,
                    0,
                    TimeSpan.FromHours(6))
            },
            "trace-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be("tenant-1");
        captured.ProviderNames.Should().Equal("ADYEN-ONLINE");
        captured.PaymentStatuses.Should().Equal(PaymentStatuses.Authorized);
        captured.CurrencyCode.Should().Be("CHF");
        captured.PaymentFlow.Should().Be(PaymentFlows.HostedCheckout);
        captured.PaymentDateFromUtc.Should().Be(
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        rateLimiter.Verify(x => x.CheckAsync(
            "tenant-1",
            "actor-1",
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Mismatched_cursor_is_rejected_before_database_query()
    {
        var repository = new Mock<IPaymentQueryRepository>();
        var service = Service(repository, AllowedRateLimiter());

        var result = await service.GetPaymentsAsync(
            new GetPaymentsRequest
            {
                After = "malformed-cursor"
            },
            "trace-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Validation);
        result.ErrorCode.Should().Be("invalid_payment_cursor");
        repository.Verify(x => x.QueryAsync(
                It.IsAny<PaymentQueryCriteria>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Cache_unavailability_fails_closed_before_database_query()
    {
        var repository = new Mock<IPaymentQueryRepository>();
        var rateLimiter = new Mock<IPaymentQueryRateLimiter>();
        rateLimiter.Setup(x => x.CheckAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRateLimitResult
            {
                IsAvailable = false,
                IsAllowed = false,
                RetryAfterSeconds = 30
            });
        var service = Service(repository, rateLimiter);

        var result = await service.GetPaymentsAsync(
            new GetPaymentsRequest(),
            "trace-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be(
            "payment_query_rate_limiter_unavailable");
        repository.Verify(x => x.QueryAsync(
                It.IsAny<PaymentQueryCriteria>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Database_failure_returns_safe_unavailable_result()
    {
        var repository = new Mock<IPaymentQueryRepository>();
        repository.Setup(x => x.QueryAsync(
                It.IsAny<PaymentQueryCriteria>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database details"));
        var service = Service(repository, AllowedRateLimiter());

        var result = await service.GetPaymentsAsync(
            new GetPaymentsRequest(),
            "trace-1",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.FailureKind.Should().Be(PaymentFailureKind.Unavailable);
        result.ErrorCode.Should().Be("payment_query_unavailable");
        result.ErrorMessage.Should().NotContain("database details");
    }

    private static PaymentQueryService Service(
        Mock<IPaymentQueryRepository> repository,
        Mock<IPaymentQueryRateLimiter> rateLimiter)
    {
        var contextResolver = new Mock<IPaymentExecutionContextResolver>();
        contextResolver.Setup(x => x.Resolve("trace-1"))
            .Returns(new PaymentContextResolution(
                new PaymentExecutionContext(
                    "tenant-1",
                    "actor-1",
                    "organization-1"),
                null));
        var codec = new PaymentQueryCursorCodec();

        return new PaymentQueryService(
            new GetPaymentsRequestValidator(),
            contextResolver.Object,
            rateLimiter.Object,
            codec,
            repository.Object,
            new PaymentQueryResponseMapper(codec),
            Mock.Of<ILogger<PaymentQueryService>>());
    }

    private static Mock<IPaymentQueryRateLimiter> AllowedRateLimiter()
    {
        var rateLimiter = new Mock<IPaymentQueryRateLimiter>();
        rateLimiter.Setup(x => x.CheckAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRateLimitResult
            {
                IsAvailable = true,
                IsAllowed = true,
                Limit = 120,
                Remaining = 119,
                ResetAfterSeconds = 1
            });

        return rateLimiter;
    }
}
