using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XUnitTest.Payment;
using Moq;
using Payment.DomainService.Entities;
using Payment.DomainService.Repositories;
using Payment.DomainService.Utilities;
using Subscription.DomainService.Entities;
using Subscription.DomainService.Messaging;
using Subscription.DomainService.Repositories;
using Subscription.DomainService.Services;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Guards the courtesy that stops a renewal failing for want of a card: the billing contact is
/// told once, ahead of time, that the card on file is about to expire — and never told twice,
/// never told about a card nobody renews off, and never left untold because the sweep crashed.
/// </summary>
public sealed class PaymentMethodExpiryEmailServiceTests
{
    private const string TenantId = "tenant-1";

    private readonly Mock<IStoredPaymentMethodRepository> _methods = new();
    private readonly Mock<IBillingAccountRepository> _accounts = new();
    private readonly Mock<IMessageClient> _messages = new();
    private readonly ControlledTimeProvider _time = new(new DateTimeOffset(2030, 3, 10, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task The_billing_contact_of_the_account_charging_the_card_is_told_once()
    {
        var card = Card("card-1", "03", "2030");
        _methods
            .Setup(repository => repository.ListExpiringAsync(
                TenantId, It.IsAny<IReadOnlyCollection<CardExpiry>>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([card]);
        _accounts
            .Setup(repository => repository.FindByDefaultPaymentMethodAsync(
                TenantId, "card-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount
            {
                ItemId = "account-1",
                OrganizationId = "org-1",
                BillingEmail = " Owner@Example.com ",
                BillingName = "Ada Lovelace",
                Language = "de-CH",
                DefaultPaymentMethodId = "card-1"
            });

        ConsumerMessage<SendMail>? queued = null;
        _messages
            .Setup(client => client.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
            .Callback<ConsumerMessage<SendMail>>(message => queued = message)
            .Returns(Task.CompletedTask);

        var sent = await Service().NotifyAsync(TenantId, CancellationToken.None);

        sent.Should().Be(1);
        queued.Should().NotBeNull("the billing contact must hear about the card before the renewal fails on it");
        queued!.ConsumerName.Should().Be(SubscriptionConstants.MailQueue);
        queued.Payload.Purpose.Should().Be(SubscriptionConstants.PaymentMethodExpiringMailPurpose);
        queued.Payload.Language.Should().Be("de-CH");
        queued.Payload.To.Should().Equal("owner@example.com");
        queued.Payload.BodyDataContext.Should().Contain(new Dictionary<string, string>
        {
            ["DisplayName"] = "Ada Lovelace",
            ["Brand"] = "visa",
            ["LastFour"] = "4242",
            ["ExpiryMonth"] = "03",
            ["ExpiryYear"] = "2030",
            ["ExpiryDate"] = "2030-03-31"
        });
        _methods.Verify(
            repository => repository.MarkExpiryNoticeSentAsync(
                TenantId, "card-1", "2030-03", It.IsAny<CancellationToken>()),
            Times.Once,
            "the card must record the expiry it was announced for, or the next pass mails again");
    }

    [Fact]
    public async Task A_card_no_account_charges_is_marked_without_a_mail()
    {
        _methods
            .Setup(repository => repository.ListExpiringAsync(
                TenantId, It.IsAny<IReadOnlyCollection<CardExpiry>>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Card("card-orphan", "3", "30")]);
        _accounts
            .Setup(repository => repository.FindByDefaultPaymentMethodAsync(
                TenantId, "card-orphan", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BillingAccount?)null);

        var sent = await Service().NotifyAsync(TenantId, CancellationToken.None);

        sent.Should().Be(0);
        _messages.VerifyNoOtherCalls();
        _methods.Verify(
            repository => repository.MarkExpiryNoticeSentAsync(
                TenantId, "card-orphan", "2030-03", It.IsAny<CancellationToken>()),
            Times.Once,
            "an orphan card left unmarked would come back every pass ahead of cards that matter");
    }

    [Fact]
    public async Task A_send_that_throws_leaves_the_card_unmarked_for_the_next_pass()
    {
        _methods
            .Setup(repository => repository.ListExpiringAsync(
                TenantId, It.IsAny<IReadOnlyCollection<CardExpiry>>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Card("card-1", "03", "2030")]);
        _accounts
            .Setup(repository => repository.FindByDefaultPaymentMethodAsync(
                TenantId, "card-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingAccount { BillingEmail = "owner@example.com" });
        _messages
            .Setup(client => client.SendToConsumerAsync(It.IsAny<ConsumerMessage<SendMail>>()))
            .ThrowsAsync(new InvalidOperationException("broker down"));

        var act = () => Service().NotifyAsync(TenantId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _methods.Verify(
            repository => repository.MarkExpiryNoticeSentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a mail that never left the process must not be recorded as sent");
    }

    [Fact]
    public void The_notice_window_covers_every_month_ending_within_it_and_no_month_already_past()
    {
        CardExpiry.EndingWithin(new DateTime(2030, 3, 1, 0, 0, 0, DateTimeKind.Utc), 30)
            .Should().Equal([new CardExpiry(2030, 3)],
                "a card expiring on 31 March is inside a 30-day window opened on 1 March");

        CardExpiry.EndingWithin(new DateTime(2030, 12, 15, 0, 0, 0, DateTimeKind.Utc), 50)
            .Should().Equal([new CardExpiry(2030, 12), new CardExpiry(2031, 1)],
                "the window must cross a year boundary without skipping January");

        CardExpiry.EndingWithin(new DateTime(2030, 3, 31, 0, 0, 0, DateTimeKind.Utc), 0)
            .Should().Equal([new CardExpiry(2030, 3)],
                "the card still works on its last day, so that day is inside any window");
    }

    [Fact]
    public void Provider_expiry_spellings_parse_to_one_key()
    {
        CardExpiry.TryParse("03", "2030", out var stripe).Should().BeTrue();
        CardExpiry.TryParse("3", "30", out var abbreviated).Should().BeTrue();
        CardExpiry.TryParse("13", "2030", out _).Should().BeFalse("there is no thirteenth month");
        CardExpiry.TryParse(null, "2030", out _).Should().BeFalse();

        stripe.Should().Be(abbreviated);
        stripe.Key.Should().Be("2030-03");
    }

    private PaymentMethodExpiryEmailService Service() => new(
        _methods.Object,
        _accounts.Object,
        _messages.Object,
        new OptionsMonitorStub(new SubscriptionOptions()),
        NullLogger<PaymentMethodExpiryEmailService>.Instance,
        _time);

    private static StoredPaymentMethod Card(string itemId, string month, string year) => new()
    {
        ItemId = itemId,
        TenantId = TenantId,
        Brand = "visa",
        LastFour = "4242",
        ExpiryMonth = month,
        ExpiryYear = year
    };

    private sealed class OptionsMonitorStub(SubscriptionOptions value) : IOptionsMonitor<SubscriptionOptions>
    {
        public SubscriptionOptions CurrentValue => value;

        public SubscriptionOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SubscriptionOptions, string?> listener) => null;
    }
}
