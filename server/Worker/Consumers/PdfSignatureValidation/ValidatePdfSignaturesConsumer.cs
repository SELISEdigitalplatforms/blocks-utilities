using Blocks.Genesis;
using Microsoft.Extensions.Options;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfSignatureValidation.Entities;
using Utility.DomainService.PdfSignatureValidation.Events;
using Utility.DomainService.PdfSignatureValidation.service;
using Utility.DomainService.PdfSignatureValidation.Utilities;
using Utility.DomainService.PdfSignatureValidation.Validator;
using Utility.DomainService.Shared.Utilities;

namespace Worker.Consumers.PdfSignatureValidation
{
    /// <summary>
    /// Validates the signatures of one PDF held in storage and records the verdict on the job.
    /// </summary>
    /// <remarks>
    /// Every write is conditional on the job's <c>RunId</c> still being the one this event was queued
    /// with (<c>UpdateJobIfCurrentRunAsync</c>), and a notification is sent only after a write that
    /// was accepted. A file re-requested while this run was working has a newer run that owns the
    /// record, so this run's result, failure and notification are all discarded rather than
    /// overwriting the newer request or telling the caller about a verdict they no longer asked for.
    /// <para>
    /// A validator that is not ready yet - still loading its trusted lists, or being restarted - is a
    /// state, not a failure. The job stays <c>Queued</c> and the event is published again with a
    /// delay, the way the financial-document delivery handler waits out an unhealthy PDF renderer.
    /// Only a job that has waited longer than <c>TrustedListWaitMinutes</c> since it was requested
    /// fails, with <c>trust_lists_unavailable</c>.
    /// </para>
    /// <para>
    /// A file DSS could not make sense of as a PDF is a <c>Failed</c> job with the validator's own
    /// error code. A file with no signature is not: that is a completed verdict of zero signatures.
    /// </para>
    /// </remarks>
    public sealed class ValidatePdfSignaturesConsumer : IConsumer<ValidatePdfSignaturesEvent>
    {
        private readonly ILogger<ValidatePdfSignaturesConsumer> _logger;
        private readonly PdfStorageHelper _storageHelper;
        private readonly IPdfSignatureValidator _validator;
        private readonly IPdfSignatureValidationRepository _repository;
        private readonly IPdfGeneratorNotificationService _notificationService;
        private readonly IMessageClient _messageClient;
        private readonly PdfSignatureValidatorOptions _options;
        private readonly TimeProvider _timeProvider;

        public ValidatePdfSignaturesConsumer(
            ILogger<ValidatePdfSignaturesConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfSignatureValidator validator,
            IPdfSignatureValidationRepository repository,
            IPdfGeneratorNotificationService notificationService,
            IMessageClient messageClient,
            IOptions<PdfSignatureValidatorOptions> options,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(options);

            _logger = logger;
            _storageHelper = storageHelper;
            _validator = validator;
            _repository = repository;
            _notificationService = notificationService;
            _messageClient = messageClient;
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        public async Task Consume(ValidatePdfSignaturesEvent @event)
        {
            ArgumentNullException.ThrowIfNull(@event);

            _logger.LogInformation(
                "ValidatePdfSignaturesConsumer: Processing file {FileId}, TenantId={TenantId}",
                LogSanitizer.Scrub(@event.FileId),
                LogSanitizer.Scrub(@event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? string.Empty));

            var job = (await _repository.GetJobsAsync([@event.FileId], @event.ProjectKey)).FirstOrDefault();

            if (job == null)
            {
                // Nothing to report against, and nobody can be polling for it.
                _logger.LogError(
                    "ValidatePdfSignaturesConsumer: No validation record for file {FileId}; skipping",
                    LogSanitizer.Scrub(@event.FileId));
                return;
            }

            if (!string.Equals(job.RunId, @event.RunId, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "ValidatePdfSignaturesConsumer: File {FileId} was requested again since run {RunId} was queued; skipping it",
                    LogSanitizer.Scrub(@event.FileId),
                    LogSanitizer.Scrub(@event.RunId));
                return;
            }

            if (job.Status is PdfIngestionStatus.Completed or PdfIngestionStatus.Failed)
            {
                // A redelivery of a message already dealt with. Brokers deliver at least once.
                _logger.LogInformation(
                    "ValidatePdfSignaturesConsumer: Run {RunId} of file {FileId} already finished; skipping the redelivery",
                    LogSanitizer.Scrub(@event.RunId),
                    LogSanitizer.Scrub(@event.FileId));
                return;
            }

            try
            {
                if (!_validator.IsReady)
                {
                    await WaitForValidatorAsync(job, @event);
                    return;
                }

                job.Status = PdfIngestionStatus.Processing;

                if (!await _repository.UpdateJobIfCurrentRunAsync(job, @event.ProjectKey))
                {
                    return;
                }

                await ValidateAsync(job, @event);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ValidatePdfSignaturesConsumer: Validation of file {FileId} threw",
                    LogSanitizer.Scrub(@event.FileId));

                await FailAsync(job, @event, "validation_error", "The validation failed unexpectedly.");
            }
        }

        private async Task ValidateAsync(PdfSignatureValidationJob job, ValidatePdfSignaturesEvent @event)
        {
            var record = await _storageHelper.GetFileRecord(job.Id, @event.ProjectKey);

            if (record == null)
            {
                await FailAsync(job, @event, "input_file_not_found", "The file could not be found in storage.");
                return;
            }

            job.FileName = record.Name;

            using var download = await _storageHelper.GetStreamForRecord(record);

            if (download == null)
            {
                await FailAsync(job, @event, "input_file_unreadable", "The file could not be downloaded.");
                return;
            }

            // The validator reads the file by path, so it is staged on local disk. It is only ever
            // read, never written, and removed whatever happens.
            var path = Path.Combine(Path.GetTempPath(), $"pdf-signature-validation-{Guid.NewGuid():N}.pdf");

            try
            {
                await using (var staged = File.Create(path))
                {
                    await download.CopyToAsync(staged);
                }

                var result = await _validator.ValidateAsync(path, CancellationToken.None);

                await ApplyAsync(job, @event, result);
            }
            finally
            {
                TryDelete(path);
            }
        }

        private async Task ApplyAsync(PdfSignatureValidationJob job, ValidatePdfSignaturesEvent @event, PdfSignatureValidatorResult result)
        {
            switch (result.Outcome)
            {
                case PdfSignatureValidatorOutcome.Verdict:
                    await CompleteAsync(job, @event, result.Verdict!);
                    break;

                case PdfSignatureValidatorOutcome.NotReady:
                    // It went down between the readiness check and the file. Nothing was decided
                    // about the file, so it goes back to waiting.
                    await WaitForValidatorAsync(job, @event);
                    break;

                case PdfSignatureValidatorOutcome.TimedOut:
                    await FailAsync(job, @event, "validation_timeout", "The validation took too long and was stopped.");
                    break;

                case PdfSignatureValidatorOutcome.Crashed:
                    // No automatic retry (spec): the file may be what crashed the validator, and
                    // retrying it would crash it again. The caller can re-request.
                    await FailAsync(job, @event, "validator_crashed", "The signature validator stopped unexpectedly.");
                    break;

                default:
                    await FailAsync(job, @event, result.ErrorCode ?? "validator_error", result.ErrorMessage ?? "The validator reported an error.");
                    break;
            }
        }

        private async Task CompleteAsync(PdfSignatureValidationJob job, ValidatePdfSignaturesEvent @event, PdfSignatureValidationVerdict verdict)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            job.Status = PdfIngestionStatus.Completed;
            job.Verdict = verdict;
            job.CompletedDate = now;
            job.LastUpdateDate = now;

            if (!await _repository.UpdateJobIfCurrentRunAsync(job, @event.ProjectKey))
            {
                return;
            }

            _logger.LogInformation(
                "ValidatePdfSignaturesConsumer: Completed file {FileId}, {SignatureCount} signature(s), allPassed={AllPassed}",
                LogSanitizer.Scrub(job.Id),
                verdict.SignatureCount,
                verdict.AllPassed);

            await NotifyAsync(job, @event, success: true);
        }

        private async Task FailAsync(PdfSignatureValidationJob job, ValidatePdfSignaturesEvent @event, string errorCode, string errorMessage)
        {
            _logger.LogError(
                "ValidatePdfSignaturesConsumer: Validation of file {FileId} failed: {ErrorCode}",
                LogSanitizer.Scrub(job.Id),
                errorCode);

            var now = _timeProvider.GetUtcNow().UtcDateTime;

            job.Status = PdfIngestionStatus.Failed;
            job.ErrorCode = errorCode;
            job.ErrorMessage = errorMessage;
            job.CompletedDate = now;
            job.LastUpdateDate = now;

            if (!await _repository.UpdateJobIfCurrentRunAsync(job, @event.ProjectKey))
            {
                return;
            }

            await NotifyAsync(job, @event, success: false);
        }

        /// <summary>
        /// Puts a job that cannot be validated yet back to waiting: <c>Queued</c>, and the same event
        /// published again after a delay. Fails the job instead once it has waited too long, or when
        /// the event cannot be published again, because then nothing would ever pick it up.
        /// </summary>
        private async Task WaitForValidatorAsync(PdfSignatureValidationJob job, ValidatePdfSignaturesEvent @event)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            if (now - job.CreateDate >= TimeSpan.FromMinutes(_options.TrustedListWaitMinutes))
            {
                await FailAsync(job, @event, "trust_lists_unavailable", "The EU trusted lists were not available in time to validate the file.");
                return;
            }

            if (job.Status != PdfIngestionStatus.Queued)
            {
                job.Status = PdfIngestionStatus.Queued;
                job.LastUpdateDate = now;

                if (!await _repository.UpdateJobIfCurrentRunAsync(job, @event.ProjectKey))
                {
                    return;
                }
            }

            try
            {
                await _messageClient.SendToConsumerAsync(
                    new ConsumerMessage<ValidatePdfSignaturesEvent>
                    {
                        ConsumerName = PdfSignatureValidationConstants.ValidatePdfSignaturesQueue,
                        Payload = @event,
                        ScheduledEnqueueTimeUtc = now.AddSeconds(_options.NotReadyRetrySeconds)
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "ValidatePdfSignaturesConsumer: Could not queue file {FileId} to try again",
                    LogSanitizer.Scrub(job.Id));

                await FailAsync(job, @event, "validation_not_queued", "The validation could not be queued to try again.");
                return;
            }

            _logger.LogInformation(
                "ValidatePdfSignaturesConsumer: The validator is not ready; file {FileId} will be tried again in {Seconds} s",
                LogSanitizer.Scrub(job.Id),
                _options.NotReadyRetrySeconds);
        }

        /// <summary>
        /// Sends the completion notification. Never lets a failure change the recorded outcome: the
        /// record is already written, and the status endpoint exists because this cannot be relied on.
        /// </summary>
        private async Task NotifyAsync(PdfSignatureValidationJob job, ValidatePdfSignaturesEvent @event, bool success)
        {
            if (string.IsNullOrEmpty(job.MessageCoRelationId))
            {
                return;
            }

            try
            {
                await _notificationService.NotifyValidatePdfSignaturesEvent(
                    success,
                    job.Id,
                    job.MessageCoRelationId,
                    job.UserId,
                    @event.ProjectKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "ValidatePdfSignaturesConsumer: Could not notify for file {FileId}; the status endpoint still has the outcome",
                    LogSanitizer.Scrub(job.Id));
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "ValidatePdfSignaturesConsumer: Could not delete the staged file");
            }
        }
    }
}
