using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sms.DomainService.Dtos;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Providers;
using Sms.DomainService.Repositories;
using Sms.DomainService.Scheduling;
using Sms.DomainService.Services;

namespace XUnitTest.Sms;

public class SmsProcessingServiceTests
{
    private const string TenantId = "tenant-a";
    private const string First = "+41790000001";
    private const string Second = "+41790000002";

    private readonly Mock<ISmsRepository> _repository = new();
    private readonly Mock<ISmsWorkQueue> _queue = new();
    private readonly Mock<ISmsProvider> _provider = new();
    private readonly SmsProviderConfiguration _configuration = new() { TenantId = TenantId, ProviderType = SmsProviderType.Twilio, MaxRetryAttempts = 3, ApiKeySecretId = "s" };
    private readonly List<string> _sentTo = [];

    public SmsProcessingServiceTests()
    {
        _provider.SetupGet(p => p.ProviderType).Returns(SmsProviderType.Twilio);
        _repository.Setup(r => r.GetActiveProviderConfigurationAsync(TenantId, It.IsAny<SmsProviderType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_configuration);
    }

    [Fact]
    public async Task ASecondDeliveryOfTheSameCommandSendsNothing()
    {
        // The claim is what serializes senders: when it returns nothing, someone else holds the lease.
        _repository.Setup(r => r.TryClaimForSendAsync(TenantId, "m1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmsMessage?)null);

        await CreateService().ProcessSendAsync(TenantId, "m1");

        _provider.Verify(p => p.SendAsync(It.IsAny<SmsProviderContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ATransientFailureRetriesOnlyThatRecipient()
    {
        var message = Message(First, Second);
        ClaimReturns(message);
        Respond(First, SmsProviderResult.Failed("busy", "429", transient: true));
        Respond(Second, SmsProviderResult.Submitted("SM2"));

        await CreateService().ProcessSendAsync(TenantId, message.ItemId);

        _sentTo.Should().Equal(First, Second);
        _queue.Verify(q => q.ScheduleAsync(TenantId, message.ItemId, It.IsAny<string>(), SmsWorkKind.Retry, It.Is<DateTime>(d => d > DateTime.UtcNow.AddSeconds(10) && d < DateTime.UtcNow.AddMinutes(2)), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.CompleteSendRoundAsync(TenantId, message.ItemId, It.IsAny<string>(), SmsMessageStatus.RetryScheduled, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()));

        // Second round: the persisted state has Second submitted, so only First goes out again.
        _sentTo.Clear();
        message.AttemptCount = 2;
        Respond(First, SmsProviderResult.Submitted("SM1"));

        await CreateService().ProcessSendAsync(TenantId, message.ItemId);

        _sentTo.Should().Equal(First);
        _repository.Verify(r => r.CompleteSendRoundAsync(TenantId, message.ItemId, It.IsAny<string>(), SmsMessageStatus.Submitted, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task APermanentFailureIsNotRetried()
    {
        var message = Message(First);
        ClaimReturns(message);
        Respond(First, SmsProviderResult.Failed("21211", "invalid number", transient: false));

        await CreateService().ProcessSendAsync(TenantId, message.ItemId);

        message.Recipients[0].Status.Should().Be(SmsRecipientStatus.Failed);
        _repository.Verify(r => r.CompleteSendRoundAsync(TenantId, message.ItemId, It.IsAny<string>(), SmsMessageStatus.Failed, "21211", It.IsAny<string?>(), It.IsAny<CancellationToken>()));
        _queue.Verify(q => q.CancelAsync(TenantId, message.ItemId, SmsWorkKind.Retry, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task TransientFailuresStopAtTheConfiguredAttemptLimit()
    {
        var message = Message(First);
        message.AttemptCount = _configuration.MaxRetryAttempts;
        ClaimReturns(message);
        Respond(First, SmsProviderResult.Failed("busy", "503", transient: true));

        await CreateService().ProcessSendAsync(TenantId, message.ItemId);

        message.Recipients[0].Status.Should().Be(SmsRecipientStatus.Failed);
        _repository.Verify(r => r.CompleteSendRoundAsync(TenantId, message.ItemId, It.IsAny<string>(), SmsMessageStatus.Failed, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task TheWatchdogIsScheduledBeforeTheLeaseIsTaken()
    {
        var order = new List<string>();
        _queue.Setup(q => q.ScheduleAsync(TenantId, "m1", It.IsAny<string>(), SmsWorkKind.Retry, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("watchdog")).Returns(Task.CompletedTask);
        _repository.Setup(r => r.TryClaimForSendAsync(TenantId, "m1", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("claim")).ReturnsAsync((SmsMessage?)null);

        await CreateService().ProcessSendAsync(TenantId, "m1");

        order.Should().Equal("watchdog", "claim");
    }

    [Theory]
    [InlineData(new[] { SmsRecipientStatus.Delivered, SmsRecipientStatus.Delivered }, SmsMessageStatus.Delivered)]
    [InlineData(new[] { SmsRecipientStatus.Delivered, SmsRecipientStatus.Undelivered }, SmsMessageStatus.PartiallyDelivered)]
    [InlineData(new[] { SmsRecipientStatus.Undelivered, SmsRecipientStatus.Failed }, SmsMessageStatus.Undelivered)]
    [InlineData(new[] { SmsRecipientStatus.DeliveryFailed, SmsRecipientStatus.Failed }, SmsMessageStatus.DeliveryFailed)]
    [InlineData(new[] { SmsRecipientStatus.Delivered, SmsRecipientStatus.Submitted }, null)]
    public void DeliveryRollup(SmsRecipientStatus[] recipients, SmsMessageStatus? expected)
    {
        SmsStatusRollup.AfterDelivery(recipients.Select(s => new SmsRecipient { Status = s }).ToList())
            .Should().Be(expected);
    }

    private SmsProcessingService CreateService()
    {
        var resolver = new Mock<ISmsProviderContextResolver>();
        resolver.Setup(r => r.ResolveAsync(TenantId, _configuration, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmsProviderContext(TenantId, _configuration, "key"));

        return new SmsProcessingService(
            _repository.Object,
            _queue.Object,
            new SmsProviderFactory([_provider.Object]),
            resolver.Object,
            new SmsRetryPolicy(),
            Mock.Of<ISmsEventPublisher>(),
            NullLogger<SmsProcessingService>.Instance);
    }

    private static SmsMessage Message(params string[] numbers) => new()
    {
        TenantId = TenantId,
        ProviderType = SmsProviderType.Twilio,
        MessageText = "hello",
        AttemptCount = 1,
        Recipients = numbers.Select(n => new SmsRecipient { Number = n }).ToList()
    };

    private void ClaimReturns(SmsMessage message) =>
        _repository.Setup(r => r.TryClaimForSendAsync(TenantId, message.ItemId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

    private void Respond(string number, SmsProviderResult result) =>
        _provider.Setup(p => p.SendAsync(It.IsAny<SmsProviderContext>(), number, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _sentTo.Add(number))
            .ReturnsAsync(result);
}
