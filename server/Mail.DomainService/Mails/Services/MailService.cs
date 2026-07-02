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
        private readonly IMailRateLimiter _mailRateLimiter;

        public MailService(
            IValidator<MailToBeSent> validator,
            IMailRepository mailRepository,
            IMailCategoryResolver mailCategoryResolver,
            IMailOutboxService mailOutboxService,
            IMailRateLimiter mailRateLimiter
        )
        {
            _validator = validator;
            _mailRepository = mailRepository;
            _mailCategoryResolver = mailCategoryResolver;
            _mailOutboxService = mailOutboxService;
            _mailRateLimiter = mailRateLimiter;
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

            var rateLimitResult = await _mailRateLimiter.CheckAsync(mailToBeSent);
            if (!rateLimitResult.IsAllowed)
            {
                return new MailMutationResponse
                {
                    IsSuccess = false,
                    IsRateLimited = true,
                    RetryAfterSeconds = rateLimitResult.RetryAfterSeconds,
                    RateLimitScope = rateLimitResult.Scope,
                    Errors = new Dictionary<string, string>
                    {
                        { "RateLimit", rateLimitResult.Reason }
                    }
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

            var organizationId = bc?.OrganizationId ?? string.Empty;
            var emailTemplate = await _mailRepository.GetEmailTemplateByPurpose(request.Purpose, request.Language, organizationId);
            var mailServerConfiguration = await _mailRepository.GetMailServerConfigurationByPurpose(request.Purpose, request.Language, organizationId);

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
                EmailTemplate = emailTemplate,
                MailServerConfiguration = mailServerConfiguration,
                IsTestMail = isTestMail,
                ProjectKey = projectKey ?? string.Empty,
                TenantId = bc?.TenantId ?? string.Empty,
                OrganizationId = organizationId,
                CreatedAtUtc = DateTime.UtcNow,
                SenderAddress = mailServerConfiguration?.SenderAddress ?? string.Empty,
                Subject = emailTemplate?.TemplateSubject ?? string.Empty,
                AllRecipients = GetDistinctRecipients(toUsers, ccUsers, bccUsers).ToList(),
            };
        }

        private static IEnumerable<string> GetDistinctRecipients(params IEnumerable<string>[] recipientGroups)
        {
            return recipientGroups
                .Where(group => group != null)
                .SelectMany(group => group)
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Distinct(StringComparer.OrdinalIgnoreCase);
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

        public async Task<GetEmailSendsResponse> GetEmailSendsAsync(GetEmailSends request)
        {
            var tenantId = BlocksContext.GetContext()?.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return new GetEmailSendsResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                    {
                        { nameof(BlocksContext), "Tenant context is missing." }
                    }
                };
            }

            request.PageSize = Math.Clamp(request.PageSize <= 0 ? 25 : request.PageSize, 1, 100);

            var result = await _mailRepository.GetEmailSendsAsync(request, tenantId);
            var items = result.Items.Take(request.PageSize).Select(MapEmailSendListItem).ToList();

            return new GetEmailSendsResponse
            {
                IsSuccess = true,
                Items = items,
                HasMore = result.HasMore,
                PageSize = request.PageSize,
                NextContinuationToken = result.HasMore && items.Count > 0
                    ? EmailSendContinuationToken.Encode(items[^1].CreatedAtUtc, items[^1].ItemId)
                    : null
            };
        }

        private static EmailSendListItem MapEmailSendListItem(MailToBeSent mailToBeSent)
        {
            return new EmailSendListItem
            {
                ItemId = mailToBeSent.ItemId ?? string.Empty,
                SenderAddress = GetSenderAddress(mailToBeSent),
                Subject = GetSubject(mailToBeSent),
                Language = mailToBeSent.Language ?? string.Empty,
                OrganizationId = mailToBeSent.OrganizationId ?? string.Empty,
                SubmissionStatus = mailToBeSent.SubmissionStatus,
                CreatedAtUtc = GetCreatedAtUtc(mailToBeSent),
                SubmittedAtUtc = mailToBeSent.SubmittedAtUtc,
                MailCategory = mailToBeSent.MailCategory,
                Recipients = GetRecipientStatuses(mailToBeSent).ToList()
            };
        }

        private static IEnumerable<EmailSendRecipientStatus> GetRecipientStatuses(MailToBeSent mailToBeSent)
        {
            var statuses = (mailToBeSent.RecipientDeliveryStatuses ?? [])
                .Where(status => !string.IsNullOrWhiteSpace(status.Recipient))
                .GroupBy(status => status.Recipient, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(status => status.CheckedAtUtc).First(), StringComparer.OrdinalIgnoreCase);

            foreach (var recipient in GetRecipientsByType(mailToBeSent.To, "To", statuses))
            {
                yield return recipient;
            }

            foreach (var recipient in GetRecipientsByType(mailToBeSent.Cc, "Cc", statuses))
            {
                yield return recipient;
            }

            foreach (var recipient in GetRecipientsByType(mailToBeSent.Bcc, "Bcc", statuses))
            {
                yield return recipient;
            }
        }

        private static IEnumerable<EmailSendRecipientStatus> GetRecipientsByType(
            IEnumerable<string>? recipients,
            string recipientType,
            IReadOnlyDictionary<string, MailRecipientDeliveryStatus> statuses)
        {
            foreach (var address in recipients ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                statuses.TryGetValue(address, out var status);
                yield return new EmailSendRecipientStatus
                {
                    Address = address,
                    RecipientType = recipientType,
                    DeliveryStatus = status?.Status ?? MailStatus.Pending,
                    StatusReason = status?.StatusReason,
                    CheckedAtUtc = status?.CheckedAtUtc
                };
            }
        }

        private static string GetSenderAddress(MailToBeSent mailToBeSent)
        {
            return !string.IsNullOrWhiteSpace(mailToBeSent.SenderAddress)
                ? mailToBeSent.SenderAddress
                : mailToBeSent.MailServerConfiguration?.SenderAddress ?? string.Empty;
        }

        private static string GetSubject(MailToBeSent mailToBeSent)
        {
            if (!string.IsNullOrWhiteSpace(mailToBeSent.Subject))
            {
                return mailToBeSent.Subject;
            }

            if (!string.IsNullOrWhiteSpace(mailToBeSent.TextSubject))
            {
                return mailToBeSent.TextSubject;
            }

            return mailToBeSent.EmailTemplate?.TemplateSubject ?? string.Empty;
        }

        private static DateTime GetCreatedAtUtc(MailToBeSent mailToBeSent)
        {
            if (mailToBeSent.CreatedAtUtc != default)
            {
                return mailToBeSent.CreatedAtUtc;
            }

            return mailToBeSent.SubmittedAtUtc
                ?? mailToBeSent.LastSubmissionAttemptAtUtc
                ?? DateTime.MinValue;
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
