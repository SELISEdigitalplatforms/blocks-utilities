using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using System.Text.RegularExpressions;

namespace Mail.DomainService.Mails
{
    public class CommonEmailValidator
    {
        private readonly IMailRepository _mailRepository;

        private static readonly Regex emailValidatorRegularExpression = new("^[a-zA-Z0-9.!#$%&’*+//=?^_`{|}~-]+@[a-zA-Z0-9-]+(?:\\.[a-zA-Z0-9-]+)*$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

        public CommonEmailValidator(IMailRepository mailRepository)
        {
            _mailRepository = mailRepository;
        }

        public bool BeValidMailArrayParameters(IEnumerable<string> replyTos)
        {
            if (replyTos == null)
            {
                return true;
            }

            var replyTosValid = replyTos.All(rt => !string.IsNullOrWhiteSpace(rt));

            return replyTosValid;
        }

        public async Task<bool> BeAnExistingFile(string fileId, CancellationToken cancellationToken)
        {
            var fileExists = await _mailRepository.FileExists(fileId);
            return fileExists;
        }

        public bool HaveAtleastOneToResipiant(IEnumerable<string> persons)
        {
            if (persons == null)
                return false;

            return persons.Any();

        }

        public bool BeExistingToRecipientPersons(IEnumerable<string> personIds)
        {
            if (personIds == null
                || !personIds.Any()
                || personIds.Any(pid => pid == null))
            {
                return false;
            }

            return true;
        }

        public bool BeExistingCcOrBccRecipientPersons(IEnumerable<string> personIds)
        {
            if (personIds == null || !personIds.Any())
            {
                return true;
            }

            if (personIds.Any(pid => pid == null))
            {
                return false;
            }

            return true;
        }

        public bool BeAVaildDataContext(MailToBeSent mailToBeSent, Dictionary<string, string> dataContext)
        {
            var template = mailToBeSent.EmailTemplate;


            if (template == null)
            {
                return false;
            }

            var matches = Regex.Matches(template.TemplateBody, @"\{{(?:.:)?(.*?)\}}", RegexOptions.None, TimeSpan.FromMilliseconds(500));

            var templatePlaceHolders = (from Match m in matches select m.Groups[1].Value).Distinct().ToList();

            if (templatePlaceHolders.Count.Equals(0))
            {
                return true;
            }

            if (templatePlaceHolders.Count > 0 && dataContext == null)
            {
                return false;
            }

            foreach (var templatePlaceHolder in templatePlaceHolders)
            {
                if (!dataContext.ContainsKey(templatePlaceHolder))
                {
                    return false;
                }
            }

            return true;
        }



        public bool HaveAValiMailServerConfiguration(MailToBeSent mailToBeSent, string purpose)
        {
            return mailToBeSent.MailServerConfiguration != null;
        }

        public bool HaveARegisteredTemplate(MailToBeSent mailToBeSent, string purpose)
        {
            return mailToBeSent.EmailTemplate != null;
        }

        public bool BeAnExistingPurposeAndLanguageCombination(MailToBeSent mailToBeSent, string purpose)
        {
            return mailToBeSent.EmailTemplate != null;
        }

        public bool HaveValidEmails(IEnumerable<string> emails)
        {
            if (emails == null)
                return true;

            foreach (var email in emails)
            {
                bool valid = IsValidEmail(email);

                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsValidEmail(string email)
        {
            return emailValidatorRegularExpression.Match(email).Success;
        }


        public bool BeValidSubjectDataContext(MailToBeSent mailToBeSent, Dictionary<string, string> subjectDataContext)
        {
            if (subjectDataContext == null || subjectDataContext.Count == 0)
            {
                return true;
            }

            var template = mailToBeSent.EmailTemplate;

            if (template == null)
            {
                return false;
            }

            var matches = Regex.Matches(template.TemplateSubject, @"\{{(?:.:)?(.*?)\}}", RegexOptions.None, TimeSpan.FromMilliseconds(500));
            var templatePlaceHolders = (from Match m in matches select m.Groups[1].Value).ToList();

            foreach (var templatePlaceHolder in templatePlaceHolders)
            {
                if (!subjectDataContext.ContainsKey(templatePlaceHolder))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
