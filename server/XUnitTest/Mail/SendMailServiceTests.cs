using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using SmtpClientKind = Mail.DomainService.Entities.SmtpClient;

namespace XUnitTest.Mail;

/// <summary>
/// Covers the send path from the queue event through to the SMTP boundary. The
/// two SMTP clients expose a virtual factory for their transport, so the tests
/// substitute it and assert on the message that would have gone out rather than
/// opening a socket.
/// </summary>
public sealed class SendMailServiceTests
{
    private readonly Mock<IMailRepository> _repository = new();

    private static MailToBeSent Mail(
        SmtpClientKind smtpClient = SmtpClientKind.Default,
        IEnumerable<string>? to = null,
        IEnumerable<string>? cc = null,
        IEnumerable<string>? bcc = null,
        IEnumerable<string>? replyTo = null) => new()
        {
            ItemId = "mail-1",
            Name = "invoice-approved",
            Language = "en",
            To = to ?? ["first@example.com", "second@example.org"],
            Cc = cc,
            Bcc = bcc,
            ReplyTo = replyTo,
            BodyDataContext = new Dictionary<string, string>
            {
                ["name"] = "Ada & Co"
            },
            SubjectDataContext = new Dictionary<string, string>
            {
                ["number"] = "42"
            },
            EmailTemplate = new EmailTemplate
            {
                Name = "invoice-approved",
                TemplateSubject = "Invoice {{number}}",
                TemplateBody = "<p>Hello {{name}}</p>"
            },
            MailServerConfiguration = new MailServerConfiguration
            {
                Host = "smtp.example",
                Port = 587,
                EnableSSL = true,
                SenderName = "Blocks",
                SenderAddress = "no-reply@example.com",
                SenderUserName = "smtp-user",
                AccountPassword = "smtp-password",
                SmtpClient = smtpClient
            }
        };

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SnsConfigurationName"] = "blocks-set"
            })
            .Build();

    /// <summary>
    /// The provider resolves the concrete SMTP clients out of the container, so
    /// the test container maps the real type onto the testable subclass.
    /// </summary>
    private SendMailService Service(TestableMicrosoftSmtpClient smtpClient)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Configuration());
        services.AddSingleton<MicrosoftSmtpClient>(smtpClient);
        services.AddTransient<MailKitSmtpClient>();

        return new SendMailService(
            NullLogger<SendMailService>.Instance,
            _repository.Object,
            new SmtpClientProvider(
                services.BuildServiceProvider(),
                NullLogger<SmtpClientProvider>.Instance));
    }

    /// <summary>
    /// Both SMTP clients stamp the ambient tenant onto the outgoing message, so
    /// the context has to exist before a send.
    /// </summary>
    private static void SetAmbientTenant() =>
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: "tenant-1",
            roles: [],
            userId: "user-1",
            userName: string.Empty,
            isAuthenticated: true,
            requestUri: string.Empty,
            organizationId: string.Empty,
            expireOn: DateTime.UtcNow.AddHours(1),
            email: string.Empty,
            permissions: [],
            phoneNumber: string.Empty,
            displayName: string.Empty,
            oauthToken: string.Empty,
            originalTenantId: "tenant-1"));

    [Fact]
    public void The_body_placeholders_are_replaced_and_html_encoded()
    {
        var body = SendMailService.BuildBody(
            "<p>Hello {{name}}</p>",
            new Dictionary<string, string> { ["name"] = "Ada & Co" });

        // Encoded so a value carrying markup cannot break out of the template.
        body.Should().Be("<p>Hello Ada &amp; Co</p>");
    }

    [Fact]
    public void An_unknown_body_placeholder_is_left_untouched()
    {
        var body = SendMailService.BuildBody(
            "<p>Hello {{name}}, order {{order}}</p>",
            new Dictionary<string, string> { ["name"] = "Ada" });

        body.Should().Be("<p>Hello Ada, order {{order}}</p>");
    }

    [Fact]
    public void The_subject_placeholders_are_replaced_without_encoding()
    {
        var subject = SendMailService.BuildSubject(
            "Invoice {{number}} for {{customer}}",
            new Dictionary<string, string>
            {
                ["number"] = "42",
                ["customer"] = "Ada & Co"
            });

        subject.Should().Be("Invoice 42 for Ada & Co");
    }

    [Fact]
    public void An_empty_data_context_leaves_the_template_alone()
    {
        SendMailService.BuildBody("<p>Hello</p>", [])
            .Should().Be("<p>Hello</p>");
        SendMailService.BuildSubject("Invoice", [])
            .Should().Be("Invoice");
    }

    [Fact]
    public void The_mail_body_combines_the_rendered_subject_and_body()
    {
        var body = Service(new TestableMicrosoftSmtpClient(Configuration()))
            .BuildMailBody(Mail());

        body.Subject.Should().Be("Invoice 42");
        body.Body.Should().Be("<p>Hello Ada &amp; Co</p>");
    }

    [Fact]
    public async Task Processing_an_event_loads_the_mail_and_hands_it_to_the_client()
    {
        SetAmbientTenant();
        _repository.Setup(x => x.GetMailToBeSent("mail-1"))
            .ReturnsAsync(Mail(SmtpClientKind.MsMailKit));
        var smtpClient = new TestableMicrosoftSmtpClient(Configuration());

        await Service(smtpClient).ProcessSendMailAsync(
            new SendEmailEvent { ItemId = "mail-1" });

        _repository.Verify(x => x.GetMailToBeSent("mail-1"), Times.Once);
        smtpClient.Subjects.Should().Equal("Invoice 42");
        smtpClient.Recipients
            .Should().Equal("first@example.com", "second@example.org");
    }

    [Fact]
    public async Task A_rejected_send_is_reported_rather_than_thrown()
    {
        SetAmbientTenant();
        _repository.Setup(x => x.GetMailToBeSent(It.IsAny<string>()))
            .ReturnsAsync(Mail(SmtpClientKind.MsMailKit));
        var smtpClient = new TestableMicrosoftSmtpClient(Configuration())
        {
            Failure = new InvalidOperationException("smtp refused")
        };

        var act = () => Service(smtpClient).ProcessSendMailAsync(
            new SendEmailEvent { ItemId = "mail-1" });

        // The queue handler logs the failure; it must not fault the consumer.
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(SmtpClientKind.MsMailKit, typeof(MicrosoftSmtpClient))]
    [InlineData(SmtpClientKind.Default, typeof(MailKitSmtpClient))]
    [InlineData(SmtpClientKind.MsGraph, typeof(MailKitSmtpClient))]
    public void The_provider_picks_the_client_named_by_the_server_configuration(
        SmtpClientKind configured,
        Type expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Configuration());
        services.AddTransient<MicrosoftSmtpClient>();
        services.AddTransient<MailKitSmtpClient>();
        var provider = new SmtpClientProvider(
            services.BuildServiceProvider(),
            NullLogger<SmtpClientProvider>.Instance);

        provider.GetSmtpClient(Mail(configured)).Should().BeOfType(expected);
    }

    [Fact]
    public async Task The_dotnet_client_addresses_every_recipient_list()
    {
        SetAmbientTenant();
        var client = new TestableMicrosoftSmtpClient(Configuration());

        var sent = await client.SendAsync(
            Mail(
                cc: ["cc@example.com"],
                bcc: ["bcc@example.com"],
                replyTo: ["reply@example.com"]),
            new MailBody { Subject = "Invoice 42", Body = "<p>Hello</p>" });

        sent.Should().BeTrue();
        client.Recipients.Should().Equal("first@example.com", "second@example.org");
        client.CarbonCopies.Should().Equal("cc@example.com");
        client.BlindCarbonCopies.Should().Equal("bcc@example.com");
        client.ReplyTos.Should().Equal("reply@example.com");
        client.Headers.Should().Contain("X-SES-CONFIGURATION-SET");
        client.Headers.Should().Contain("X-Tenant-Id");
    }

    [Fact]
    public async Task The_dotnet_client_tolerates_absent_optional_recipient_lists()
    {
        SetAmbientTenant();
        var client = new TestableMicrosoftSmtpClient(Configuration());

        var sent = await client.SendAsync(
            Mail(),
            new MailBody { Subject = "Invoice 42", Body = "<p>Hello</p>" });

        sent.Should().BeTrue();
        client.CarbonCopies.Should().BeEmpty();
        client.BlindCarbonCopies.Should().BeEmpty();
    }

    [Fact]
    public async Task A_dotnet_transport_failure_is_reported_as_not_sent()
    {
        SetAmbientTenant();
        var client = new TestableMicrosoftSmtpClient(Configuration())
        {
            Failure = new InvalidOperationException("smtp refused")
        };

        var sent = await client.SendAsync(
            Mail(),
            new MailBody { Subject = "Invoice 42", Body = "<p>Hello</p>" });

        sent.Should().BeFalse();
    }

    [Fact]
    public async Task The_mailkit_client_connects_authenticates_sends_and_disconnects()
    {
        SetAmbientTenant();
        var transport = new RecordingMailKitClient();
        var client = new TestableMailKitSmtpClient(Configuration(), transport);

        var sent = await client.SendAsync(
            Mail(
                cc: ["cc@example.com"],
                bcc: ["bcc@example.com"],
                replyTo: ["reply@example.com"]),
            new MailBody { Subject = "Invoice 42", Body = "<p>Hello</p>" });

        sent.Should().BeTrue();
        transport.Host.Should().Be("smtp.example");
        transport.Port.Should().Be(587);
        transport.UseSsl.Should().BeTrue();
        transport.UserName.Should().Be("smtp-user");
        transport.Disconnected.Should().BeTrue();
        transport.Message!.Subject.Should().Be("Invoice 42");
        transport.Message.To.Count.Should().Be(2);
        transport.Message.Cc.Count.Should().Be(1);
        transport.Message.Bcc.Count.Should().Be(1);
        transport.Message.ReplyTo.Count.Should().Be(1);
        transport.Message.Headers["X-Tenant-Id"].Should().Be("tenant-1");
    }

    [Fact]
    public async Task A_mailkit_connect_failure_is_reported_as_not_sent()
    {
        SetAmbientTenant();
        var transport = new RecordingMailKitClient
        {
            Failure = new InvalidOperationException("connection refused")
        };
        var client = new TestableMailKitSmtpClient(Configuration(), transport);

        var sent = await client.SendAsync(
            Mail(),
            new MailBody { Subject = "Invoice 42", Body = "<p>Hello</p>" });

        sent.Should().BeFalse();
        transport.Disconnected.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_recipient_stops_the_mailkit_send_before_connecting()
    {
        SetAmbientTenant();
        var transport = new RecordingMailKitClient();
        var client = new TestableMailKitSmtpClient(Configuration(), transport);

        var act = () => client.SendAsync(
            Mail(to: [string.Empty]),
            new MailBody { Subject = "Invoice 42", Body = "<p>Hello</p>" });

        // Address parsing happens before the connection, so a bad recipient
        // never reaches the SMTP server.
        await act.Should().ThrowAsync<ParseException>();
        transport.Host.Should().BeNull();
    }

    [Fact]
    public async Task The_mailkit_client_tolerates_absent_optional_recipient_lists()
    {
        SetAmbientTenant();
        var transport = new RecordingMailKitClient();
        var client = new TestableMailKitSmtpClient(Configuration(), transport);

        var sent = await client.SendAsync(
            Mail(),
            new MailBody { Subject = "Invoice 42", Body = "<p>Hello</p>" });

        sent.Should().BeTrue();
        transport.Message!.Cc.Count.Should().Be(0);
        transport.Message.Bcc.Count.Should().Be(0);
        transport.Message.ReplyTo.Count.Should().Be(0);
    }

    [Fact]
    public void The_email_validator_accepts_a_complete_mail()
    {
        var result = Validator().Validate(Mail());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_mail_without_recipients_is_rejected()
    {
        var result = Validator().Validate(Mail(to: []));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorMessage == "There is no recipient");
    }

    [Fact]
    public void A_blank_entry_in_a_recipient_list_is_rejected()
    {
        var mail = Mail(cc: ["cc@example.com", " "]);

        var result = Validator().Validate(mail);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorMessage == "Empty item found in Cc");
    }

    [Fact]
    public void A_mail_without_a_language_is_rejected()
    {
        var mail = Mail();
        mail.Language = string.Empty;

        var result = Validator().Validate(mail);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(mail.Language));
    }

    [Fact]
    public void A_mail_without_a_registered_template_is_rejected()
    {
        var mail = Mail();
        mail.EmailTemplate = null!;

        var result = Validator().Validate(mail);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(mail.Name));
    }

    [Fact]
    public void A_mail_whose_body_context_misses_a_placeholder_is_rejected()
    {
        var mail = Mail();
        mail.BodyDataContext = [];

        var result = Validator().Validate(mail);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(mail.BodyDataContext));
    }

    [Fact]
    public void A_test_mail_skips_the_body_context_check()
    {
        var mail = Mail();
        mail.BodyDataContext = [];
        mail.IsTestMail = true;

        var result = Validator().Validate(mail);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_mail_without_a_server_configuration_is_rejected()
    {
        var mail = Mail();
        mail.MailServerConfiguration = null!;

        var result = Validator().Validate(mail);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.ErrorMessage.Contains(
                "email server configuration",
                StringComparison.Ordinal));
    }

    private IValidator<MailToBeSent> Validator() =>
        new EmailValidator(new CommonEmailValidator(_repository.Object));

    [Theory]
    [InlineData("amqp://<username>:<password>@localhost:5672")]
    [InlineData("amqps://<username>:<password>@rabbit.example:5671")]
    [InlineData("AMQP://guest:guest@localhost:5672")]
    public void An_amqp_connection_string_selects_the_rabbitmq_transport(
        string connectionString)
    {
        var configuration =
            CommunicationConstants.GetMessageConfiguration(connectionString);

        configuration.RabbitMqConfiguration.Should().NotBeNull();
        configuration.AzureServiceBusConfiguration.Should().BeNull();
    }

    [Theory]
    [InlineData("Endpoint=sb://blocks.servicebus.windows.net/;SharedAccessKeyName=x")]
    [InlineData("")]
    [InlineData("not a uri")]
    [InlineData("https://blocks.example")]
    public void Anything_else_falls_back_to_azure_service_bus(
        string connectionString)
    {
        var configuration =
            CommunicationConstants.GetMessageConfiguration(connectionString);

        configuration.AzureServiceBusConfiguration.Should().NotBeNull();
        configuration.AzureServiceBusConfiguration!.Queues
            .Should().Contain(CommunicationConstants.MailQueueName);
        configuration.RabbitMqConfiguration.Should().BeNull();
    }

    private sealed class TestableMicrosoftSmtpClient : MicrosoftSmtpClient
    {
        public TestableMicrosoftSmtpClient(IConfiguration configuration)
            : base(NullLogger<MicrosoftSmtpClient>.Instance, configuration)
        {
        }

        public Exception? Failure { get; init; }

        public List<string> Recipients { get; } = [];

        public List<string> CarbonCopies { get; } = [];

        public List<string> BlindCarbonCopies { get; } = [];

        public List<string> ReplyTos { get; } = [];

        public List<string> Headers { get; } = [];

        public List<string> Subjects { get; } = [];

        protected override INetSmtpClient CreateSmtpClient(
            MailServerConfiguration config) =>
            new Transport(this);

        private sealed class Transport : INetSmtpClient
        {
            private readonly TestableMicrosoftSmtpClient _owner;

            public Transport(TestableMicrosoftSmtpClient owner) => _owner = owner;

            public Task SendMailAsync(System.Net.Mail.MailMessage message)
            {
                if (_owner.Failure != null)
                {
                    throw _owner.Failure;
                }

                _owner.Recipients.AddRange(
                    message.To.Select(address => address.Address));
                _owner.CarbonCopies.AddRange(
                    message.CC.Select(address => address.Address));
                _owner.BlindCarbonCopies.AddRange(
                    message.Bcc.Select(address => address.Address));
                _owner.ReplyTos.AddRange(
                    message.ReplyToList.Select(address => address.Address));
                _owner.Headers.AddRange(message.Headers.AllKeys!);
                _owner.Subjects.Add(message.Subject);

                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }
    }

    private sealed class TestableMailKitSmtpClient : MailKitSmtpClient
    {
        private readonly RecordingMailKitClient _transport;

        public TestableMailKitSmtpClient(
            IConfiguration configuration,
            RecordingMailKitClient transport)
            : base(NullLogger<MailKitSmtpClient>.Instance, configuration) =>
            _transport = transport;

        protected override IMailKitSmtpClient CreateSmtpClient() => _transport;
    }

    private sealed class RecordingMailKitClient : IMailKitSmtpClient
    {
        public Exception? Failure { get; init; }

        public string? Host { get; private set; }

        public int Port { get; private set; }

        public bool UseSsl { get; private set; }

        public string? UserName { get; private set; }

        public MimeMessage? Message { get; private set; }

        public bool Disconnected { get; private set; }

        public Task ConnectAsync(string host, int port, bool useSsl)
        {
            if (Failure != null)
            {
                throw Failure;
            }

            Host = host;
            Port = port;
            UseSsl = useSsl;

            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(string userName, string password)
        {
            UserName = userName;

            return Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message)
        {
            Message = message;

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(bool quit)
        {
            Disconnected = true;

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
