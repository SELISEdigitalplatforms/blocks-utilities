using Mail.DomainService.Entities;
using FluentValidation;

namespace Mail.DomainService.Mails
{
    public class EmailValidator : AbstractValidator<MailToBeSent>
    {
        public EmailValidator(CommonEmailValidator commonEmailValidator)
        {
            RuleFor(r => r.To)
                .Cascade(CascadeMode.Stop)
                .Must(commonEmailValidator.HaveAtleastOneToResipiant)
                .WithMessage("There is no recipient")
                .Must(commonEmailValidator.BeExistingToRecipientPersons)
                .WithMessage("One or more of the provided To persons do not exist");

            RuleFor(r => r.Bcc)
                .Cascade(CascadeMode.Stop)
                .Must(commonEmailValidator.BeExistingCcOrBccRecipientPersons)
                .WithMessage("One or more of the provided Bcc persons do not exist")
                .Must(commonEmailValidator.BeValidMailArrayParameters)
                .WithMessage("Empty item found in Bcc");

            RuleFor(r => r.Cc)
                .Cascade(CascadeMode.Stop)
                .Must(commonEmailValidator.BeExistingCcOrBccRecipientPersons)
                .WithMessage("One or more of the provided Cc persons do not exist")
                .Must(commonEmailValidator.BeValidMailArrayParameters)
                .WithMessage("Empty item found in Cc");


            RuleFor(r => r.Language).Cascade(CascadeMode.Stop)
             .NotEmpty().NotNull().WithMessage("Language is require");

            RuleFor(r => r.Name).Cascade(CascadeMode.Stop)
                .NotEmpty().NotNull().WithMessage("Purpose is require")
                .Must(commonEmailValidator.BeAnExistingPurposeAndLanguageCombination).WithMessage("The purpose and language combination does not exist. Please check the EmailTemplates collection")
                .Must(commonEmailValidator.HaveARegisteredTemplate).WithMessage("The purpose and language combination does not have a registered email template. Please check the EmailTemplates collection")
                .Must(commonEmailValidator.HaveAValiMailServerConfiguration).WithMessage("The purpose and language combination does not have a registered email server configuration. Please check the MailServerConfiguration collection");

            RuleFor(r => r.BodyDataContext)
                .Must(commonEmailValidator.BeAVaildDataContext)
                .WithMessage("Data context does not have some values needed for the targeted template")
                .When(mailToBeSent => mailToBeSent.EmailTemplate != null && !mailToBeSent.IsTestMail);

            RuleFor(r => r.SubjectDataContext).Must(commonEmailValidator.BeValidSubjectDataContext).WithMessage("Subject datacontext is invalid");

            RuleFor(r => r.ReplyTo).Must(commonEmailValidator.BeValidMailArrayParameters).WithMessage("Empty item found in ReplyTo");

            RuleFor(r => r.Attachments).Must(commonEmailValidator.BeValidMailArrayParameters).WithMessage("Empty item found in Attachments");
        }
    }
}
