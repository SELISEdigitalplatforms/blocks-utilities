using FluentAssertions;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Requests;
using Sms.DomainService.Responses;
using Sms.DomainService.Validators;

namespace XUnitTest.Sms;

public class SmsProviderConfigurationTests
{
    private readonly SaveSmsProviderConfigurationRequestValidator _validator = new();

    private static SaveSmsProviderConfigurationRequest ValidTwilio() => new()
    {
        Name = "Primary",
        ProviderType = SmsProviderType.Twilio,
        SenderNumber = "+15005550006",
        AccountId = "AC" + new string('a', 32),
        ApiKey = "token",
        StatusCallbackBaseUrl = "https://utilities.example.com"
    };

    [Fact]
    public void Validator_AcceptsAValidTwilioConfiguration()
    {
        _validator.Validate(ValidTwilio()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_RequiresApiKeyOnCreateButNotOnUpdate()
    {
        var create = ValidTwilio();
        create.ApiKey = null;
        var update = ValidTwilio();
        update.ApiKey = null;
        update.ConfigurationId = "existing";

        _validator.Validate(create).IsValid.Should().BeFalse();
        _validator.Validate(update).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("AC123")]
    [InlineData("")]
    public void Validator_RejectsMalformedTwilioAccountSid(string accountSid)
    {
        var request = ValidTwilio();
        request.AccountId = accountSid;

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RequiresTelnyxProfileAndWebhookKey()
    {
        var request = ValidTwilio();
        request.ProviderType = SmsProviderType.Telnyx;
        request.AccountId = null;

        var result = _validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName).Should().Contain(["MessagingProfileId", "WebhookPublicKey"]);
    }

    [Theory]
    [InlineData("http://utilities.example.com")]
    [InlineData("not-a-url")]
    public void Validator_RequiresHttpsCallbackBase(string url)
    {
        var request = ValidTwilio();
        request.StatusCallbackBaseUrl = url;

        _validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_RejectsOutOfRangeLimits()
    {
        var request = ValidTwilio();
        request.MaxRetryAttempts = 0;
        request.RateLimit = new SmsRateLimitSettings { TenantMaxPerWindow = 0 };
        request.SpamFilter = new SmsSpamFilterSettings { MaxRecipients = 0 };

        var properties = _validator.Validate(request).Errors.Select(e => e.PropertyName).ToList();

        properties.Should().Contain(["MaxRetryAttempts", "RateLimit.TenantMaxPerWindow", "SpamFilter.MaxRecipients"]);
    }

    [Theory]
    [InlineData(null, "ACME", true)]
    [InlineData("+15005550006", "ACME Bank", true)]
    [InlineData(null, null, false)]
    [InlineData("0791234567", null, false)]
    [InlineData(null, "123456", false)]
    [InlineData(null, "TwelveChars1", false)]
    [InlineData(null, "ACME-Bank", false)]
    public void Validator_SenderNeedsAValidNumberOrName(string? number, string? name, bool valid)
    {
        var request = ValidTwilio();
        request.SenderNumber = number;
        request.SenderName = name;

        _validator.Validate(request).IsValid.Should().Be(valid);
    }

    [Theory]
    [InlineData("+41790000000", "+15005550006", null, "+15005550006")]
    [InlineData("+41790000000", "+15005550006", "ACME", "ACME")]
    [InlineData("+41790000000", "+15005550006", "  ", "+15005550006")]
    [InlineData("+14155550100", "+15005550006", "ACME", "+15005550006")]
    [InlineData("+14155550100", "", "ACME", null)]
    [InlineData("+41790000000", "", "ACME", "ACME")]
    public void SenderNameIsUsedUnlessTheDestinationRejectsIt(string to, string number, string? name, string? expectedFrom)
    {
        new SmsProviderConfiguration { SenderNumber = number, SenderName = name }.ResolveFrom(to).Should().Be(expectedFrom);
    }

    [Fact]
    public void ExcludedPrefixesAreConfigurable()
    {
        var configuration = new SmsProviderConfiguration { SenderNumber = "+15005550006", SenderName = "ACME", SenderNameExcludedPrefixes = ["+86"] };

        configuration.ResolveFrom("+8613800000000").Should().Be("+15005550006");
        configuration.ResolveFrom("+14155550100").Should().Be("ACME");
    }

    [Theory]
    [InlineData("+1", true)]
    [InlineData("+593", true)]
    [InlineData("1", false)]
    [InlineData("+12345", false)]
    public void Validator_ExcludedPrefixesMustBeCountryCodes(string prefix, bool valid)
    {
        var request = ValidTwilio();
        request.SenderNameExcludedPrefixes = [prefix];

        _validator.Validate(request).IsValid.Should().Be(valid);
    }

    [Fact]
    public void View_NeverCarriesTheSecret()
    {
        var view = SmsProviderConfigurationView.From(new SmsProviderConfiguration { ApiKeySecretId = "secret-id" });

        view.HasApiKey.Should().BeTrue();
        typeof(SmsProviderConfigurationView).GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(name => name.Contains("Secret") || name == "ApiKey" || name.Contains("Token"));
    }
}
