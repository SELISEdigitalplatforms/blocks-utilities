using FluentAssertions;
using Moq;
using Sms.DomainService.Entities;
using Sms.DomainService.Repositories;
using Sms.DomainService.Requests;
using Sms.DomainService.Services;
using Sms.DomainService.Utilities;
using Sms.DomainService.Validators;

namespace XUnitTest.Sms;

public class SmsTemplateTests
{
    private const string TenantId = "tenant-a";
    private readonly Mock<ISmsRepository> _repository = new();

    [Fact]
    public void Renderer_FillsPlaceholdersIgnoringCaseAndInnerSpaces()
    {
        var body = SmsTemplateRenderer.Render("Hi {{ Name }}, your code is {{code}}.", new Dictionary<string, string> { ["name"] = "Ada", ["CODE"] = "42" }, out var missing);

        body.Should().Be("Hi Ada, your code is 42.");
        missing.Should().BeEmpty();
    }

    [Fact]
    public void Renderer_ReportsEachMissingKeyOnce()
    {
        SmsTemplateRenderer.Render("{{a}} {{b}} {{a}}", new Dictionary<string, string>(), out var missing);

        missing.Should().Equal("a", "b");
    }

    [Fact]
    public void Placeholders_AreListedForThePortal()
    {
        SmsTemplateRenderer.Placeholders("Dear {{name}}, {{ amount }} due {{Name}}").Should().Equal("name", "amount");
    }

    [Theory]
    [InlineData("otp", "en-US", "Code {{code}}", true)]
    [InlineData("otp code", "en-US", "x", false)]
    [InlineData("otp", "english", "x", false)]
    [InlineData("otp", "en", "", false)]
    public void Validator(string name, string language, string body, bool valid)
    {
        new SaveSmsTemplateRequestValidator()
            .Validate(new SaveSmsTemplateRequest { Name = name, Language = language, Body = body })
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Save_RefusesASecondTemplateWithTheSameNameAndLanguage()
    {
        _repository.Setup(r => r.GetTemplateAsync(TenantId, "otp", "en-US", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SmsTemplate { ItemId = "existing", TenantId = TenantId, Name = "otp", Language = "en-US" });

        using var tenant = SmsTenantContext.Enter(TenantId);
        var result = await Service().SaveAsync(new SaveSmsTemplateRequest { Name = "otp", Language = "en-US", Body = "{{code}}" });

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainKey("Name");
        _repository.Verify(r => r.SaveTemplateAsync(It.IsAny<SmsTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Save_UpdatingATemplateKeepsItsIdAndMayKeepItsName()
    {
        var existing = new SmsTemplate { ItemId = "t1", TenantId = TenantId, Name = "otp", Language = "en-US", Body = "old" };
        _repository.Setup(r => r.GetTemplateByIdAsync(TenantId, "t1", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _repository.Setup(r => r.GetTemplateAsync(TenantId, "otp", "en-US", It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        using var tenant = SmsTenantContext.Enter(TenantId);
        var result = await Service().SaveAsync(new SaveSmsTemplateRequest { TemplateId = "t1", Name = "otp", Language = "en-US", Body = "Code {{code}}" });

        result.IsSuccess.Should().BeTrue();
        result.Template!.ItemId.Should().Be("t1");
        result.Template.Placeholders.Should().Equal("code");
        _repository.Verify(r => r.SaveTemplateAsync(It.Is<SmsTemplate>(t => t.ItemId == "t1" && t.Body == "Code {{code}}"), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Save_UpdateOfAnUnknownTemplateFails()
    {
        using var tenant = SmsTenantContext.Enter(TenantId);
        var result = await Service().SaveAsync(new SaveSmsTemplateRequest { TemplateId = "nope", Name = "otp", Language = "en-US", Body = "x" });

        result.Errors.Should().ContainKey("TemplateId");
    }

    [Fact]
    public async Task List_ClampsThePageSize()
    {
        _repository.Setup(r => r.ListTemplatesAsync(TenantId, null, null, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<SmsTemplate>(), 0L));

        using var tenant = SmsTenantContext.Enter(TenantId);
        var result = await Service().ListAsync(new GetSmsTemplatesRequest { Page = 0, PageSize = 5000 });

        result.PageSize.Should().Be(100);
        result.Page.Should().Be(1);
    }

    private SmsTemplateService Service() => new(new SaveSmsTemplateRequestValidator(), _repository.Object);
}
