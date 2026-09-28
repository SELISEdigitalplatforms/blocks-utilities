using FluentAssertions;
using Sms.DomainService.Entities;
using Sms.DomainService.Enums;
using Sms.DomainService.Providers;

namespace XUnitTest.Sms;

public class SmsProviderFactoryTests
{
    [Fact]
    public void SmsProviderFactory_ShouldFailClearlyForUnknownProvider()
    {
        var factory = new SmsProviderFactory([]);

        var act = () => factory.GetProvider(new SmsProviderConfiguration { ProviderType = SmsProviderType.Twilio });

        act.Should().Throw<InvalidOperationException>().WithMessage("*not registered*");
    }
}
