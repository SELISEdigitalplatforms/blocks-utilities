using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Moq;

namespace XUnitTest.Mail
{
    public class CommonEmailValidatorTests
    {
        private readonly Mock<IMailRepository> _mailRepository;
        private readonly CommonEmailValidator _validator;

        public CommonEmailValidatorTests()
        {
            _mailRepository = new Mock<IMailRepository>();
            _validator = new CommonEmailValidator(_mailRepository.Object);
        }

        [Fact]
        public void BeValidMailArrayParameters_WhenNull_ReturnsTrue()
        {
            _validator.BeValidMailArrayParameters(null!).Should().BeTrue();
        }

        [Fact]
        public void BeValidMailArrayParameters_WhenAllNonEmpty_ReturnsTrue()
        {
            _validator.BeValidMailArrayParameters(new[] { "a@b.com", "c@d.com" }).Should().BeTrue();
        }

        [Fact]
        public void BeValidMailArrayParameters_WhenContainsWhitespace_ReturnsFalse()
        {
            _validator.BeValidMailArrayParameters(new[] { "a@b.com", "  " }).Should().BeFalse();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task BeAnExistingFile_ReturnsRepositoryResult(bool exists)
        {
            _mailRepository.Setup(r => r.FileExists("file-1")).ReturnsAsync(exists);

            var result = await _validator.BeAnExistingFile("file-1", CancellationToken.None);

            result.Should().Be(exists);
            _mailRepository.Verify(r => r.FileExists("file-1"), Times.Once);
        }

        [Fact]
        public void HaveAtleastOneToResipiant_WhenNull_ReturnsFalse()
        {
            _validator.HaveAtleastOneToResipiant(null!).Should().BeFalse();
        }

        [Fact]
        public void HaveAtleastOneToResipiant_WhenEmpty_ReturnsFalse()
        {
            _validator.HaveAtleastOneToResipiant(Array.Empty<string>()).Should().BeFalse();
        }

        [Fact]
        public void HaveAtleastOneToResipiant_WhenHasItems_ReturnsTrue()
        {
            _validator.HaveAtleastOneToResipiant(new[] { "p1" }).Should().BeTrue();
        }

        [Fact]
        public void BeExistingToRecipientPersons_WhenNull_ReturnsFalse()
        {
            _validator.BeExistingToRecipientPersons(null!).Should().BeFalse();
        }

        [Fact]
        public void BeExistingToRecipientPersons_WhenEmpty_ReturnsFalse()
        {
            _validator.BeExistingToRecipientPersons(Array.Empty<string>()).Should().BeFalse();
        }

        [Fact]
        public void BeExistingToRecipientPersons_WhenContainsNull_ReturnsFalse()
        {
            _validator.BeExistingToRecipientPersons(new[] { "p1", null! }).Should().BeFalse();
        }

        [Fact]
        public void BeExistingToRecipientPersons_WhenAllValid_ReturnsTrue()
        {
            _validator.BeExistingToRecipientPersons(new[] { "p1", "p2" }).Should().BeTrue();
        }

        [Fact]
        public void BeExistingCcOrBccRecipientPersons_WhenNull_ReturnsTrue()
        {
            _validator.BeExistingCcOrBccRecipientPersons(null!).Should().BeTrue();
        }

        [Fact]
        public void BeExistingCcOrBccRecipientPersons_WhenEmpty_ReturnsTrue()
        {
            _validator.BeExistingCcOrBccRecipientPersons(Array.Empty<string>()).Should().BeTrue();
        }

        [Fact]
        public void BeExistingCcOrBccRecipientPersons_WhenContainsNull_ReturnsFalse()
        {
            _validator.BeExistingCcOrBccRecipientPersons(new[] { "p1", null! }).Should().BeFalse();
        }

        [Fact]
        public void BeExistingCcOrBccRecipientPersons_WhenAllValid_ReturnsTrue()
        {
            _validator.BeExistingCcOrBccRecipientPersons(new[] { "p1" }).Should().BeTrue();
        }

        [Fact]
        public void BeAVaildDataContext_WhenTemplateNull_ReturnsFalse()
        {
            var mail = new MailToBeSent { EmailTemplate = null };

            _validator.BeAVaildDataContext(mail, new Dictionary<string, string>()).Should().BeFalse();
        }

        [Fact]
        public void BeAVaildDataContext_WhenNoPlaceholders_ReturnsTrue()
        {
            var mail = new MailToBeSent { EmailTemplate = new EmailTemplate { TemplateBody = "Hello there" } };

            _validator.BeAVaildDataContext(mail, null!).Should().BeTrue();
        }

        [Fact]
        public void BeAVaildDataContext_WhenPlaceholdersButDataContextNull_ReturnsFalse()
        {
            var mail = new MailToBeSent { EmailTemplate = new EmailTemplate { TemplateBody = "Hi {{FirstName}}" } };

            _validator.BeAVaildDataContext(mail, null!).Should().BeFalse();
        }

        [Fact]
        public void BeAVaildDataContext_WhenPlaceholderMissing_ReturnsFalse()
        {
            var mail = new MailToBeSent { EmailTemplate = new EmailTemplate { TemplateBody = "Hi {{FirstName}}" } };

            _validator.BeAVaildDataContext(mail, new Dictionary<string, string> { { "LastName", "x" } }).Should().BeFalse();
        }

        [Fact]
        public void BeAVaildDataContext_WhenAllPlaceholdersPresent_ReturnsTrue()
        {
            var mail = new MailToBeSent { EmailTemplate = new EmailTemplate { TemplateBody = "Hi {{FirstName}} {{LastName}}" } };

            var dataContext = new Dictionary<string, string> { { "FirstName", "a" }, { "LastName", "b" } };

            _validator.BeAVaildDataContext(mail, dataContext).Should().BeTrue();
        }

        [Fact]
        public void HaveAValiMailServerConfiguration_WhenNull_ReturnsFalse()
        {
            var mail = new MailToBeSent { MailServerConfiguration = null };

            _validator.HaveAValiMailServerConfiguration(mail, "purpose").Should().BeFalse();
        }

        [Fact]
        public void HaveAValiMailServerConfiguration_WhenPresent_ReturnsTrue()
        {
            var mail = new MailToBeSent { MailServerConfiguration = new MailServerConfiguration() };

            _validator.HaveAValiMailServerConfiguration(mail, "purpose").Should().BeTrue();
        }

        [Fact]
        public void HaveARegisteredTemplate_ReflectsTemplatePresence()
        {
            _validator.HaveARegisteredTemplate(new MailToBeSent { EmailTemplate = null }, "p").Should().BeFalse();
            _validator.HaveARegisteredTemplate(new MailToBeSent { EmailTemplate = new EmailTemplate() }, "p").Should().BeTrue();
        }

        [Fact]
        public void BeAnExistingPurposeAndLanguageCombination_ReflectsTemplatePresence()
        {
            _validator.BeAnExistingPurposeAndLanguageCombination(new MailToBeSent { EmailTemplate = null }, "p").Should().BeFalse();
            _validator.BeAnExistingPurposeAndLanguageCombination(new MailToBeSent { EmailTemplate = new EmailTemplate() }, "p").Should().BeTrue();
        }

        [Fact]
        public void HaveValidEmails_WhenNull_ReturnsTrue()
        {
            _validator.HaveValidEmails(null!).Should().BeTrue();
        }

        [Fact]
        public void HaveValidEmails_WhenAllValid_ReturnsTrue()
        {
            _validator.HaveValidEmails(new[] { "a@b.com", "c.d@example.org" }).Should().BeTrue();
        }

        [Fact]
        public void HaveValidEmails_WhenAnyInvalid_ReturnsFalse()
        {
            _validator.HaveValidEmails(new[] { "a@b.com", "not-an-email" }).Should().BeFalse();
        }

        [Theory]
        [InlineData("user@example.com", true)]
        [InlineData("first.last@sub.example.co", true)]
        [InlineData("plainaddress", false)]
        [InlineData("missingatsign.com", false)]
        [InlineData("@no-local.com", false)]
        public void IsValidEmail_ValidatesFormat(string email, bool expected)
        {
            _validator.IsValidEmail(email).Should().Be(expected);
        }

        [Fact]
        public void BeValidSubjectDataContext_WhenNull_ReturnsTrue()
        {
            _validator.BeValidSubjectDataContext(new MailToBeSent(), null!).Should().BeTrue();
        }

        [Fact]
        public void BeValidSubjectDataContext_WhenEmpty_ReturnsTrue()
        {
            _validator.BeValidSubjectDataContext(new MailToBeSent(), new Dictionary<string, string>()).Should().BeTrue();
        }

        [Fact]
        public void BeValidSubjectDataContext_WhenTemplateNull_ReturnsFalse()
        {
            var mail = new MailToBeSent { EmailTemplate = null };
            var context = new Dictionary<string, string> { { "Name", "x" } };

            _validator.BeValidSubjectDataContext(mail, context).Should().BeFalse();
        }

        [Fact]
        public void BeValidSubjectDataContext_WhenPlaceholderMissing_ReturnsFalse()
        {
            var mail = new MailToBeSent { EmailTemplate = new EmailTemplate { TemplateSubject = "Welcome {{Name}}" } };
            var context = new Dictionary<string, string> { { "Other", "x" } };

            _validator.BeValidSubjectDataContext(mail, context).Should().BeFalse();
        }

        [Fact]
        public void BeValidSubjectDataContext_WhenPlaceholderPresent_ReturnsTrue()
        {
            var mail = new MailToBeSent { EmailTemplate = new EmailTemplate { TemplateSubject = "Welcome {{Name}}" } };
            var context = new Dictionary<string, string> { { "Name", "x" } };

            _validator.BeValidSubjectDataContext(mail, context).Should().BeTrue();
        }
    }
}
