using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using Utility.DomainService.PdfGenerator.service;

namespace Worker.Consumers.PdfGenerator
{
    /// <summary>
    /// Base class for PDF stamping consumers - eliminates duplication in stamp processing initialization
    /// </summary>
    /// <typeparam name="TEvent">The stamp event type</typeparam>
    [ExcludeFromCodeCoverage]
    public abstract class StampPdfConsumerBase<TEvent> where TEvent : class
    {
        protected readonly ILogger _logger;
        protected readonly PdfStorageHelper _storageHelper;
        protected readonly IPdfEngineProvider _engineProvider;
        protected readonly IPdfGeneratorNotificationService _notificationService;

        protected StampPdfConsumerBase(
            ILogger logger,
            PdfStorageHelper storageHelper,
            IPdfEngineProvider engineProvider,
            IPdfGeneratorNotificationService notificationService)
        {
            _logger = logger;
            _storageHelper = storageHelper;
            _engineProvider = engineProvider;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Gets the PDF stream and engine for stamping, handling errors and logging
        /// </summary>
        /// <returns>Tuple with (pdfStream, engine) or (null, null) if failed</returns>
        protected async Task<(Stream? pdfStream, IPdfEngine? engine)> InitializeStampingAsync(
            string pdfFileId,
            string? projectKey,
            int engineId,
            string messageCoRelationId,
            int stampCount)
        {
            var tenantId = projectKey ?? BlocksContext.GetContext()?.TenantId ?? "";
            _logger.LogInformation("{ConsumerName}: Processing event for MessageCoRelationId={MessageCoRelationId}, TenantId={TenantId}, StampCount={StampCount}", GetConsumerName(), messageCoRelationId, tenantId, stampCount);

            try
            {
                // Get engine from event
                var engine = _engineProvider.GetEngine(engineId);

                // Get original PDF
                _logger.LogInformation("{ConsumerName}: Getting PDF stream for PdfFileId={PdfFileId}", GetConsumerName(), pdfFileId);
                var pdfStream = await _storageHelper.GetPdfStream(pdfFileId, projectKey);
                
                if (pdfStream == null)
                {
                    _logger.LogError("{ConsumerName}: Failed to get PDF stream for PdfFileId={PdfFileId}", GetConsumerName(), pdfFileId);
                    return (null, null);
                }

                return (pdfStream, engine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ConsumerName}: Error initializing stamping for PdfFileId={PdfFileId}", GetConsumerName(), pdfFileId);
                return (null, null);
            }
        }

        /// <summary>
        /// Gets the consumer name for logging purposes
        /// </summary>
        protected abstract string GetConsumerName();
    }
}
