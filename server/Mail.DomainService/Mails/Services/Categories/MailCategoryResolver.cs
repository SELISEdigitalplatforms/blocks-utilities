using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Services.Categories
{
    public class MailCategoryResolver : IMailCategoryResolver
    {
        private const long DefaultLargeAttachmentThresholdInBytes = 3L * 1024 * 1024;

        private readonly IMailAttachmentMetadataProvider _attachmentMetadataProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MailCategoryResolver> _logger;

        public MailCategoryResolver(
            IMailAttachmentMetadataProvider attachmentMetadataProvider,
            IConfiguration configuration,
            ILogger<MailCategoryResolver> logger)
        {
            _attachmentMetadataProvider = attachmentMetadataProvider;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<MailCategory> ResolveAsync(MailToBeSent mailToBeSent, CancellationToken cancellationToken = default)
        {
            var attachmentIds = mailToBeSent.Attachments?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            if (attachmentIds.Length == 0)
            {
                return MailCategory.NoAttachment;
            }

            var thresholdInBytes = GetLargeAttachmentThresholdInBytes();

            foreach (var attachmentId in attachmentIds)
            {
                var metadata = await _attachmentMetadataProvider.GetMetadataAsync(attachmentId, cancellationToken);

                if (!metadata.SizeInBytes.HasValue)
                {
                    _logger.LogInformation("Attachment size is unknown. Routing mail to large attachment lane. FileId={FileId}", attachmentId);
                    return MailCategory.LargeAttachment;
                }

                if (metadata.SizeInBytes.Value > thresholdInBytes)
                {
                    _logger.LogInformation(
                        "Large attachment detected. Routing mail to large attachment lane. FileId={FileId}, SizeInBytes={SizeInBytes}, ThresholdInBytes={ThresholdInBytes}",
                        attachmentId,
                        metadata.SizeInBytes.Value,
                        thresholdInBytes);
                    return MailCategory.LargeAttachment;
                }
            }

            return MailCategory.SmallAttachment;
        }

        private long GetLargeAttachmentThresholdInBytes()
        {
            var thresholdInMb = _configuration.GetValue<int?>("MailCategory:LargeAttachmentThresholdInMb") ?? 3;

            return thresholdInMb <= 0
                ? DefaultLargeAttachmentThresholdInBytes
                : thresholdInMb * 1024L * 1024L;
        }

    }
}
