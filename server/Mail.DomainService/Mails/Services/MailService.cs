using Blocks.Genesis;
using FluentValidation;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Utilities;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails
{
    public class MailService : IMailService
    {
        private readonly IValidator<MailToBeSent> _validator;
        private readonly IMessageClient _messageClient;
        private readonly IMailRepository _mailRepository;

        public MailService(
            IValidator<MailToBeSent> validator,
            IMessageClient messageClient,
            IMailRepository mailRepository
        )
        {
            _validator = validator;
            _messageClient = messageClient;
            _mailRepository = mailRepository;
        }

        public async Task<BaseMutationResponse> ProcessMailToAnyAsync(SendMailToAny request)
        {
            var mailToBeSent = await MapAsync(request, false, request?.IsTestMail ?? false);
            return await ProcessMailSent(mailToBeSent);
        }

        public async Task<BaseMutationResponse> ProcessMailAsync(SendMail request)
        {
            var onlyUser = request.SendPhoneNumberAsEmail == true ? false : true;
            var mailToBeSent = await MapAsync(request, onlyUser);
            return await ProcessMailSent(mailToBeSent);
        }

        public async Task<BaseMutationResponse> ProcessMailSent(MailToBeSent mailToBeSent)
        {
            var validationResult = await _validator.ValidateAsync(mailToBeSent);
            if (!validationResult.IsValid)
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var result = await SaveMailToBeSent(mailToBeSent);
            return new BaseMutationResponse
            {
                IsSuccess = result
            };
        }

        public async Task<MailToBeSent> MapAsync(BaseMailRequest request, bool onlyUser = true, bool isTestMail = false)
        {
            var bc = BlocksContext.GetContext();
            var toUsers = request.To;
            var ccUsers = request.Cc;
            var bccUsers = request.Bcc;

            if (onlyUser)
            {
                toUsers = await _mailRepository.GetEmailAdressOfUsers(request.To);
                ccUsers = await _mailRepository.GetEmailAdressOfUsers(request.Cc);
                bccUsers = await _mailRepository.GetEmailAdressOfUsers(request.Bcc);
            }

            return new MailToBeSent
            {
                ItemId = Guid.NewGuid().ToString(),
                To = toUsers,
                Cc = ccUsers,
                Bcc = bccUsers,

                BodyDataContext = request.BodyDataContext,
                Name = request.Purpose,
                Language = request.Language,
                Attachments = request.Attachments ?? Enumerable.Empty<string>(),// new string[] { },
                ReplyTo = request.ReplyTo,
                SubjectDataContext = request.SubjectDataContext,
                EmailTemplate = await _mailRepository.GetEmailTemplateByPurpose(request.Purpose, request.Language, bc.OrganizationId),
                MailServerConfiguration = await _mailRepository.GetMailServerConfigurationByPurpose(request.Purpose, request.Language, bc.OrganizationId),
                IsTestMail = isTestMail
            };
        }

        public async Task<bool> SaveMailToBeSent(MailToBeSent mailToBeSent)
        {
            var result = await _mailRepository.SaveMailToBeSent(mailToBeSent);

            await SendToQueue(mailToBeSent.ItemId);

            return result;
        }

        public async Task<bool> SendToQueue(string itemId)
        {
            await _messageClient.SendToConsumerAsync<SendEmailEvent>(new ConsumerMessage<SendEmailEvent>
            {
                ConsumerName = CommunicationConstants.MailQueueName,
                Payload = new SendEmailEvent
                {
                    ItemId = itemId,
                }
            });

            return true;
        }

        public async Task<GetMailBoxMailsResponse> GetMailBoxMailsAsync(GetMailBoxMails request)
        {

            if (!string.IsNullOrEmpty(request.Status) &&
                (!Enum.TryParse<MailStatus>(request.Status, true, out var status) ||
                 !CommunicationConstants.AllowedFilterStatuses.Contains(status)))
            {
                var allowed = string.Join(", ", CommunicationConstants.AllowedFilterStatuses);
                return new GetMailBoxMailsResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                     {
                         { "Status", $"Invalid status: {request.Status}. Allowed values are: {allowed}" }
                     }
                };
            }

            var (mails, count) = await _mailRepository.GetMailBoxAggregatedMails(request);
            return new GetMailBoxMailsResponse
            {
                IsSuccess = true,
                Mails = mails,
                TotalCount = count
            };
        }

        public async Task<GetMailBoxMailResponse> GetMailBoxMailAsync(GetMailBoxMail request)
        {
            var mail = await _mailRepository.GetMailBoxMail(request.MessageId, request.ProjectKey);
            if (mail == null)
            {
                return new GetMailBoxMailResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "MessageId", "Mail not found" } }
                };
            }
            return new GetMailBoxMailResponse
            {
                IsSuccess = true,
                Mail = mail
            };
        }
    }
}
