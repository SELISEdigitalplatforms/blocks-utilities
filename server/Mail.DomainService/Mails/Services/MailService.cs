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
        private readonly IMailRepository _mailRepository;
        private readonly IMailCategoryResolver _mailCategoryResolver;
        private readonly IMailOutboxService _mailOutboxService;

        public MailService(
            IValidator<MailToBeSent> validator,
            IMailRepository mailRepository,
            IMailCategoryResolver mailCategoryResolver,
            IMailOutboxService mailOutboxService
        )
        {
            _validator = validator;
            _mailRepository = mailRepository;
            _mailCategoryResolver = mailCategoryResolver;
            _mailOutboxService = mailOutboxService;
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
            var projectKey = request is IProjectKey projectRequest
                ? projectRequest.ProjectKey
                : null;            

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
                IsTestMail = isTestMail,
                ProjectKey = projectKey ?? string.Empty,
                TenantId = bc?.TenantId ?? string.Empty,
                OrganizationId = bc?.OrganizationId ?? string.Empty,
            };
        }

        public async Task<bool> SaveMailToBeSent(MailToBeSent mailToBeSent)
        {
            mailToBeSent.MailCategory = await _mailCategoryResolver.ResolveAsync(mailToBeSent);
            mailToBeSent.SubmissionStatus = MailSubmissionStatus.Queued;
            var outboxMessage = CreateSendMailOutboxMessage(mailToBeSent);

            var result = await _mailRepository.SaveMailToBeSentWithOutboxAsync(mailToBeSent, outboxMessage);

            return result;
        }

        public async Task SendToQueueAsync<T>(string queue, T payload) where T : class
        {
            await _mailOutboxService.EnqueueAsync(Guid.NewGuid().ToString(), queue, payload, $"{typeof(T).Name}:{Guid.NewGuid()}");
        }

        private MailOutboxMessage CreateSendMailOutboxMessage(MailToBeSent mailToBeSent)
        {
            var deduplicationKey = $"mail-send:{mailToBeSent.ItemId}:attempt:1";
            MailOutboxMessage outboxMessage = mailToBeSent.MailCategory switch
            {
                MailCategory.SmallAttachment => _mailOutboxService.CreateMessage(
                    mailToBeSent.ItemId,
                    CommunicationConstants.SmallAttachmentMailQueueName,
                    CreateSendCommand<SmallAttachmentSendEmailCommand>(mailToBeSent, 1),
                    deduplicationKey),
                MailCategory.LargeAttachment => _mailOutboxService.CreateMessage(
                    mailToBeSent.ItemId,
                    CommunicationConstants.LargeAttachmentMailQueueName,
                    CreateSendCommand<LargeAttachmentSendEmailCommand>(mailToBeSent, 1),
                    deduplicationKey),
                _ => _mailOutboxService.CreateMessage(
                    mailToBeSent.ItemId,
                    CommunicationConstants.NoAttachmentMailQueueName,
                    CreateSendCommand<NoAttachmentSendEmailCommand>(mailToBeSent, 1),
                    deduplicationKey)
            };

            outboxMessage.ProjectKey = mailToBeSent.ProjectKey;
            outboxMessage.TenantId = mailToBeSent.TenantId;
            outboxMessage.OrganizationId = mailToBeSent.OrganizationId;

            return outboxMessage;
        }

        private static TCommand CreateSendCommand<TCommand>(MailToBeSent mailToBeSent, int attempt)
            where TCommand : SendEmailCommand, new()
        {
            return new TCommand
            {
                ItemId = mailToBeSent.ItemId,
                Attempt = attempt,
                ProjectKey = mailToBeSent.ProjectKey,
                TenantId = mailToBeSent.TenantId,
                OrganizationId = mailToBeSent.OrganizationId
            };
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
