using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Mails;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Mail;

public sealed class MailControllerTests
{
    [Fact]
    public async Task SendToAny_returns_ok_when_service_succeeds()
    {
        var service = new Mock<IMailService>();
        service.Setup(x => x.ProcessMailToAnyAsync(It.IsAny<SendMailToAny>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });
        var controller = new MailController(service.Object);

        var result = await controller.SendToAny(new SendMailToAny());

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task SendToAny_returns_bad_request_when_service_fails()
    {
        var service = new Mock<IMailService>();
        service.Setup(x => x.ProcessMailToAnyAsync(It.IsAny<SendMailToAny>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });
        var controller = new MailController(service.Object);

        var result = await controller.SendToAny(new SendMailToAny());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Send_returns_ok_when_service_succeeds()
    {
        var service = new Mock<IMailService>();
        service.Setup(x => x.ProcessMailAsync(It.IsAny<SendMail>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });
        var controller = new MailController(service.Object);

        var result = await controller.Send(new SendMail());

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Send_returns_bad_request_when_service_fails()
    {
        var service = new Mock<IMailService>();
        service.Setup(x => x.ProcessMailAsync(It.IsAny<SendMail>()))
            .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });
        var controller = new MailController(service.Object);

        var result = await controller.Send(new SendMail());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetMailBoxMails_returns_ok_when_service_succeeds()
    {
        var service = new Mock<IMailService>();
        service.Setup(x => x.GetMailBoxMailsAsync(It.IsAny<GetMailBoxMails>()))
            .ReturnsAsync(new GetMailBoxMailsResponse { IsSuccess = true });
        var controller = new MailController(service.Object);

        var result = await controller.GetMailBoxMails(new GetMailBoxMails());

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMailBoxMails_returns_bad_request_when_service_fails()
    {
        var service = new Mock<IMailService>();
        service.Setup(x => x.GetMailBoxMailsAsync(It.IsAny<GetMailBoxMails>()))
            .ReturnsAsync(new GetMailBoxMailsResponse { IsSuccess = false });
        var controller = new MailController(service.Object);

        var result = await controller.GetMailBoxMails(new GetMailBoxMails());

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetMailBoxMail_returns_ok_when_service_succeeds()
    {
        var service = new Mock<IMailService>();
        service.Setup(x => x.GetMailBoxMailAsync(It.IsAny<GetMailBoxMail>()))
            .ReturnsAsync(new GetMailBoxMailResponse { IsSuccess = true });
        var controller = new MailController(service.Object);

        var result = await controller.GetMailBoxMail(new GetMailBoxMail());

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetMailBoxMail_returns_bad_request_when_service_fails()
    {
        var service = new Mock<IMailService>();
        service.Setup(x => x.GetMailBoxMailAsync(It.IsAny<GetMailBoxMail>()))
            .ReturnsAsync(new GetMailBoxMailResponse { IsSuccess = false });
        var controller = new MailController(service.Object);

        var result = await controller.GetMailBoxMail(new GetMailBoxMail());

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
