using FluentAssertions;
using Sms.DomainService.Requests;
using Sms.DomainService.Validators;

namespace XUnitTest.Sms;

public class SmsRequestValidatorTests
{
    [Fact]
    public void SendSmsRequestValidator_ShouldRejectMissingRecipient()
    {
        var validator = new SendSmsRequestValidator();

        var result = validator.Validate(new SendSmsRequest { MessageText = "hello" });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void SendSmsRequestValidator_ShouldRejectInvalidPhoneNumber()
    {
        var validator = new SendSmsRequestValidator();

        var result = validator.Validate(new SendSmsRequest { MessageText = "hello", DestinationNumbers = ["abc"] });

        result.IsValid.Should().BeFalse();
    }
}
