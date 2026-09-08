using Blocks.Genesis;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.PdfGenerator.Tooling;
using Utility.DomainService.PdfIngestion;
using Utility.DomainService.PdfIngestion.Entities;
using Utility.DomainService.PdfIngestion.Events;
using Utility.DomainService.PdfIngestion.service;
using Utility.DomainService.Shared.Utilities;

namespace Worker.Consumers.PdfIngestion
{
    /// <summary>
    /// Inspects one PDF held in storage and remediates whatever the inspection calls for, replacing
    /// the file in place when a stage actually changed its bytes.
    /// </summary>
    /// <remarks>
    /// Every exit path writes the ingestion record, the same discipline
    /// <c>ConvertDocumentToPdfConsumer</c> follows: the completion notification can be missed, and
    /// the record is the only thing the status endpoint can answer from.
    /// <para>
    /// A file the pipeline could not make sense of at all is still a <c>Completed</c> job with a
    /// verdict attached (<c>IsReadable: false</c>) - the pipeline ran and answered the question it
    /// exists to answer. <c>Failed</c> is reserved for this consumer itself never reaching a verdict:
    /// storage unreachable, an unhandled exception, or a repaired file that could not be saved back.
    /// </para>
    /// </remarks>
    [ExcludeFromCodeCoverage]
    public class IngestPdfConsumer : IConsumer<IngestPdfEvent>
    {
        private readonly ILogger<IngestPdfConsumer> _logger;
        private readonly PdfStorageHelper _storageHelper;
        private readonly IPdfIngestionPipeline _pipeline;
        private readonly IPdfIngestionRepository _repository;
        private readonly IPdfGeneratorNotificationService _notificationService;

        public IngestPdfConsumer(
            ILogger<IngestPdfConsumer> logger,
            PdfStorageHelper storageHelper,
            IPdfIngestionPipeline pipeline,
            IPdfIngestionRepository repository,
            IPdfGeneratorNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _pipeline = pipeline;
            _repository = repository;
            _notificationService = notificationService;
        }

        public async Task Consume(IngestPdfEvent @event)
        {
            var tenantId = @event.ProjectKey ?? BlocksContext.GetContext()?.TenantId ?? string.Empty;

            _logger.LogInformation(
                "IngestPdfConsumer: Processing ingestion of file {FileId}, TenantId={TenantId}",
                LogSanitizer.Scrub(@event.FileId),
                LogSanitizer.Scrub(tenantId));

            var job = await _repository.GetJobAsync(@event.FileId, @event.ProjectKey);

            if (job == null)
            {
                // Nothing to report progress against, and no caller can be polling for it. Running
                // the pipeline anyway would produce a verdict with nowhere to record it.
                _logger.LogError(
                    "IngestPdfConsumer: No ingestion record for file {FileId}; skipping",
                    LogSanitizer.Scrub(@event.FileId));

                return;
            }

            try
            {
                job.Status = PdfIngestionStatus.Processing;
                await _repository.UpdateJobAsync(job, @event.ProjectKey);

                await IngestAsync(job, @event);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "IngestPdfConsumer: Ingestion of file {FileId} threw",
                    LogSanitizer.Scrub(@event.FileId));

                await Fail(job, @event, "ingestion_error", "The ingestion failed unexpectedly.");
            }
        }

        private async Task IngestAsync(PdfIngestionJob job, IngestPdfEvent @event)
        {
            var record = await _storageHelper.GetFileRecord(job.Id, @event.ProjectKey);

            if (record == null)
            {
                await Fail(job, @event, "input_file_not_found", "The file could not be found in storage.");
                return;
            }

            job.FileName = record.Name;

            using var fileStream = await _storageHelper.GetStreamForRecord(record);

            if (fileStream == null)
            {
                await Fail(job, @event, "input_file_unreadable", "The file could not be downloaded.");
                return;
            }

            using var buffer = new MemoryStream();
            await fileStream.CopyToAsync(buffer);
            var inputBytes = buffer.ToArray();

            var outcome = await _pipeline.ProcessAsync(inputBytes, CancellationToken.None);

            if (outcome.ContentChanged && !@event.DryRun)
            {
                var metadata = new Dictionary<string, string>
                {
                    { "IngestedFileId", job.Id },
                    { "IngestedDate", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                    { "FileType", "IngestedPDF" }
                };

                var saved = await _storageHelper.SavePdfToStorage(
                    new MemoryStream(outcome.FinalBytes),
                    job.Id,
                    job.FileName ?? $"{job.Id}.pdf",
                    metadata,
                    record.ParentDirectoryID ?? string.Empty,
                    @event.ProjectKey);

                if (!saved)
                {
                    // The verdict describes bytes that were never actually written back, so the
                    // stored record would disagree with the file a caller downloads next - a real
                    // job failure, not merely a note on an otherwise-completed verdict.
                    await Fail(job, @event, "output_not_saved", "The remediated PDF could not be written back to storage.");
                    return;
                }
            }

            job.Status = PdfIngestionStatus.Completed;
            job.Verdict = PdfIngestionVerdict.FromOutcome(outcome);
            job.CompletedDate = DateTime.UtcNow;
            await _repository.UpdateJobAsync(job, @event.ProjectKey);

            _logger.LogInformation(
                "IngestPdfConsumer: Completed ingestion of file {FileId}, contentChanged={ContentChanged}",
                LogSanitizer.Scrub(job.Id),
                outcome.ContentChanged);

            await Notify(job, @event, success: outcome.IsReadable);
        }

        private async Task Fail(PdfIngestionJob job, IngestPdfEvent @event, string errorCode, string errorMessage)
        {
            _logger.LogError(
                "IngestPdfConsumer: Ingestion of file {FileId} failed: {ErrorCode}",
                LogSanitizer.Scrub(job.Id),
                errorCode);

            job.Status = PdfIngestionStatus.Failed;
            job.ErrorCode = errorCode;
            job.ErrorMessage = errorMessage;
            job.CompletedDate = DateTime.UtcNow;

            await _repository.UpdateJobAsync(job, @event.ProjectKey);
            await Notify(job, @event, success: false);
        }

        /// <summary>
        /// Sends the completion notification. Never lets a notification failure change the recorded
        /// outcome - the record is already written, and the status endpoint exists precisely because
        /// this call cannot be relied on.
        /// </summary>
        private async Task Notify(PdfIngestionJob job, IngestPdfEvent @event, bool success)
        {
            try
            {
                await _notificationService.NotifyIngestPdfEvent(
                    success,
                    job.Id,
                    job.MessageCoRelationId ?? string.Empty,
                    job.UserId,
                    @event.ProjectKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "IngestPdfConsumer: Could not notify for file {FileId}; status endpoint still has the outcome",
                    LogSanitizer.Scrub(job.Id));
            }
        }
    }
}
