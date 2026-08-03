using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Template;
using Mail.DomainService.Template.Models;
using Mail.DomainService.Template.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Mail;

public sealed class TemplateControllerTests
{
    [Fact]
    public async Task Save_returns_ok_when_service_succeeds()
    {
        var service = new Mock<ITemplateService>();
        service.Setup(x => x.SaveTemplateAsync(It.IsAny<Template>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });
        var controller = new TemplateController(service.Object);

        var result = await controller.Save(new Template());

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Save_returns_bad_request_when_service_fails()
    {
        var service = new Mock<ITemplateService>();
        service.Setup(x => x.SaveTemplateAsync(It.IsAny<Template>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });
        var controller = new TemplateController(service.Object);

        var result = await controller.Save(new Template());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Get_returns_template_when_found()
    {
        var service = new Mock<ITemplateService>();
        var template = new EmailTemplate();
        service.Setup(x => x.GetAsync(It.IsAny<GetTemplate>())).ReturnsAsync(template);
        var controller = new TemplateController(service.Object);

        var result = await controller.Get(new GetTemplate());

        result.Should().BeSameAs(template);
    }

    [Fact]
    public async Task Get_returns_null_when_not_found()
    {
        var service = new Mock<ITemplateService>();
        service.Setup(x => x.GetAsync(It.IsAny<GetTemplate>())).ReturnsAsync((EmailTemplate?)null);
        var controller = new TemplateController(service.Object);

        var result = await controller.Get(new GetTemplate());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Gets_returns_service_response()
    {
        var service = new Mock<ITemplateService>();
        var expected = new GetAllTemplatesResponse();
        service.Setup(x => x.GetAllTemplatesAsync(It.IsAny<GetAllTemplates>())).ReturnsAsync(expected);
        var controller = new TemplateController(service.Object);

        var result = await controller.Gets(new GetAllTemplates());

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task Clone_returns_ok_when_service_succeeds()
    {
        var service = new Mock<ITemplateService>();
        service.Setup(x => x.CloneTemplateAsync(It.IsAny<CloneTemplateRequest>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });
        var controller = new TemplateController(service.Object);

        var result = await controller.Clone(new CloneTemplateRequest());

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Clone_returns_bad_request_when_service_fails()
    {
        var service = new Mock<ITemplateService>();
        service.Setup(x => x.CloneTemplateAsync(It.IsAny<CloneTemplateRequest>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });
        var controller = new TemplateController(service.Object);

        var result = await controller.Clone(new CloneTemplateRequest());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Delete_returns_bad_request_when_item_id_missing()
    {
        var service = new Mock<ITemplateService>();
        var controller = new TemplateController(service.Object);

        var result = await controller.Delete(new DeleteTemplateRequest { ItemId = "  " });

        result.Should().BeOfType<BadRequestObjectResult>();
        service.Verify(x => x.DeleteAsync(It.IsAny<DeleteTemplateRequest>()), Times.Never);
    }

    [Fact]
    public async Task Delete_returns_ok_when_service_succeeds()
    {
        var service = new Mock<ITemplateService>();
        service.Setup(x => x.DeleteAsync(It.IsAny<DeleteTemplateRequest>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });
        var controller = new TemplateController(service.Object);

        var result = await controller.Delete(new DeleteTemplateRequest { ItemId = "item-1" });

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Delete_returns_bad_request_when_service_fails()
    {
        var service = new Mock<ITemplateService>();
        service.Setup(x => x.DeleteAsync(It.IsAny<DeleteTemplateRequest>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });
        var controller = new TemplateController(service.Object);

        var result = await controller.Delete(new DeleteTemplateRequest { ItemId = "item-1" });

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LoadTemplatePluginToken_returns_ok_when_token_available()
    {
        var service = new Mock<ITemplateService>();
        service.Setup(x => x.GetTemplatePluginTokenAsync("bee", "u-1"))
            .ReturnsAsync(new BeeLoginResponse { AccessToken = "token" });
        var controller = new TemplateController(service.Object);

        var result = await controller.LoadTemplatePluginToken("bee", "u-1");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task LoadTemplatePluginToken_returns_bad_request_when_token_missing()
    {
        var service = new Mock<ITemplateService>();
        service.Setup(x => x.GetTemplatePluginTokenAsync("bee", "u-1"))
            .ReturnsAsync((BeeLoginResponse?)null);
        var controller = new TemplateController(service.Object);

        var result = await controller.LoadTemplatePluginToken("bee", "u-1");

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
