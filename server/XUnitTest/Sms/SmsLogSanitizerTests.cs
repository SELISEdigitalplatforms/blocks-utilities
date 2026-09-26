using FluentAssertions;
using Sms.DomainService.Requests;
using Sms.DomainService.Utilities;
using Sms.DomainService.Validators;

namespace XUnitTest.Sms;

public class SmsLogSanitizerTests
{
    [Fact]
    public void Id_CannotForgeANewLogEntry()
    {
        var forged = "abc\r\n2026-09-26 INFO SmsService: accepted TenantId=victim";

        var logged = SmsLogSanitizer.Id(forged);

        logged.Should().NotContainAny("\r", "\n", " ", "=", ":");
        logged.Should().StartWith("abc2026-09-26INFOSmsService");
    }

    [Theory]
    [InlineData(null, "missing")]
    [InlineData("  ", "missing")]
    [InlineData("\r\n", "missing")]
    [InlineData("!!", "invalid")]
    [InlineData("tenant_a-1.x", "tenant_a-1.x")]
    public void Id_KeepsIdentifiersSearchable(string? value, string expected)
    {
        SmsLogSanitizer.Id(value).Should().Be(expected);
    }

    [Fact]
    public void Id_IsBounded()
    {
        SmsLogSanitizer.Id(new string('a', 500)).Should().HaveLength(128);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("order-42.retry_1", true)]
    [InlineData("line\nbreak", false)]
    [InlineData("has space", false)]
    public void CorrelationId_IsAnIdentifierOrNothing(string? correlationId, bool valid)
    {
        new SendSmsRequestValidator()
            .Validate(new SendSmsRequest { DestinationNumbers = ["+41790000000"], MessageText = "hi", CorrelationId = correlationId })
            .IsValid.Should().Be(valid);
        new SendSmsByTemplateRequestValidator()
            .Validate(new SendSmsByTemplateRequest { DestinationNumbers = ["+41790000000"], TemplateName = "otp", CorrelationId = correlationId })
            .IsValid.Should().Be(valid);
    }
}
