using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Providers;

namespace XUnitTest.Sms;

public class TwilioSmsProviderTests
{
    [Fact]
    public async Task SendAsync_ShouldFailPermanently_WhenSenderIsDisplayName()
    {
        var provider = new TwilioSmsProvider(NullLogger<TwilioSmsProvider>.Instance);
        var message = new SmsMessage
        {
            ItemId = Guid.NewGuid().ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            DestinationNumbers = ["+15551234567"],
            MessageText = "Hello"
        };
        var configuration = new SmsProviderConfiguration
        {
            ProviderType = SmsProviderType.Twilio,
            AccountId = "AC00000000000000000000000000000000",
            AuthToken = "token",
            Sender = "SELISE Signature"
        };

        var result = await provider.SendAsync(message, configuration);

        result.IsSuccess.Should().BeFalse();
        result.IsTransientFailure.Should().BeFalse();
        result.ErrorCode.Should().Be("twilio_invalid_sender");
    }

    [Theory]
    [InlineData("SELISE APP", true)]
    [InlineData("SELISE", true)]
    [InlineData("SELISE123", true)]
    [InlineData("SELISE Signature", false)]
    [InlineData("SELISE_APP", false)]
    public void IsAlphaSenderId_ShouldAllowRegisteredTwilioAlphaSenderIdsWithSpaces(string sender, bool expected)
    {
        var method = typeof(TwilioSmsProvider).GetMethod("IsAlphaSenderId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull();
        var actual = (bool)method!.Invoke(null, [sender])!;

        actual.Should().Be(expected);
    }}
