using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Utility.DomainService.PdfGenerator.Utilities;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Renders one HTML document to PDF in-process via PuppeteerSharp and stores the result
    /// before returning, so the caller gets a File ID in the same HTTP response.
    /// </summary>
    public class SinglePdfGeneratorService : ISinglePdfGeneratorService
    {
        public const int MaxHtmlContentBytes = 2 * 1024 * 1024;
        public const int MaxRenderedPdfBytes = 10 * 1024 * 1024;
        public static readonly TimeSpan DefaultRenderTimeout = TimeSpan.FromSeconds(30);

        private readonly PuppeteerSharpEngine _engine;
        private readonly PdfStorageHelper _storageHelper;
        private readonly ILogger<SinglePdfGeneratorService> _logger;

        /// <summary>
        /// Wall-clock bound for the in-process render. Overridable in tests so the timeout path
        /// does not wait a full 30 seconds. DI leaves this at <see cref="DefaultRenderTimeout"/>.
        /// </summary>
        public TimeSpan RenderTimeout { get; init; } = DefaultRenderTimeout;

        public SinglePdfGeneratorService(
            PuppeteerSharpEngine engine,
            PdfStorageHelper storageHelper,
            ILogger<SinglePdfGeneratorService> logger)
        {
            _engine = engine;
            _storageHelper = storageHelper;
            _logger = logger;
        }

        public async Task<CreateSinglePdfFromHtmlResponse> CreateSinglePdfFromHtmlAsync(
            CreateSinglePdfFromHtmlRequest request)
        {
            var correlationId = request.MessageCoRelationId;

            _logger.LogInformation(
                "CreateSinglePdfFromHtmlAsync started for MessageCoRelationId: {MessageCoRelationId}, ProjectKey: {ProjectKey}",
                LogSanitizer.Scrub(correlationId),
                LogSanitizer.Scrub(request.ProjectKey));

            var validationFailure = ValidateRequest(request);
            if (validationFailure != null)
            {
                return Fail(validationFailure, correlationId);
            }

            var options = BuildPdfOptions(request);
            var accessModifier = ResolveAccessModifier(request.AccessModifier);
            var fileId = string.IsNullOrWhiteSpace(request.OutputFileId)
                ? Guid.NewGuid().ToString()
                : request.OutputFileId.Trim();

            Stream? pdfStream;
            try
            {
                pdfStream = await RenderWithTimeoutAsync(request.HtmlContent, options);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(
                    "CreateSinglePdfFromHtmlAsync timed out after 30 seconds for MessageCoRelationId: {MessageCoRelationId}",
                    LogSanitizer.Scrub(correlationId));
                return Fail("PDF render timed out after 30 seconds", correlationId);
            }

            if (pdfStream == null || pdfStream.Length == 0)
            {
                pdfStream?.Dispose();
                _logger.LogWarning(
                    "CreateSinglePdfFromHtmlAsync render returned empty stream for MessageCoRelationId: {MessageCoRelationId}",
                    LogSanitizer.Scrub(correlationId));
                return Fail("PDF render failed", correlationId);
            }

            await using (pdfStream)
            {
                if (pdfStream.Length > MaxRenderedPdfBytes)
                {
                    _logger.LogWarning(
                        "CreateSinglePdfFromHtmlAsync rendered PDF exceeds {MaxBytes} bytes for MessageCoRelationId: {MessageCoRelationId}",
                        MaxRenderedPdfBytes,
                        LogSanitizer.Scrub(correlationId));
                    return Fail($"Rendered PDF exceeds maximum size of {MaxRenderedPdfBytes} bytes", correlationId);
                }

                var saved = await _storageHelper.SavePdfToStorage(
                    pdfStream,
                    fileId,
                    request.OutputFileName,
                    metadata: null,
                    parentDirectoryId: PdfGeneratorConstants.SinglePdfGeneratedFilesDirectory,
                    projectKey: request.ProjectKey,
                    accessModifier: accessModifier);

                if (!saved)
                {
                    _logger.LogError(
                        "CreateSinglePdfFromHtmlAsync storage upload failed for FileId: {FileId}, MessageCoRelationId: {MessageCoRelationId}",
                        LogSanitizer.Scrub(fileId),
                        LogSanitizer.Scrub(correlationId));
                    return Fail("Storage upload failed", correlationId);
                }
            }

            _logger.LogInformation(
                "CreateSinglePdfFromHtmlAsync completed for FileId: {FileId}, MessageCoRelationId: {MessageCoRelationId}",
                LogSanitizer.Scrub(fileId),
                LogSanitizer.Scrub(correlationId));

            return new CreateSinglePdfFromHtmlResponse
            {
                IsSuccess = true,
                FileId = fileId,
                Message = "PDF created",
                MessageCoRelationId = correlationId
            };
        }

        private static string? ValidateRequest(CreateSinglePdfFromHtmlRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectKey))
            {
                return "ProjectKey is required";
            }

            if (string.IsNullOrWhiteSpace(request.HtmlContent))
            {
                return "HtmlContent is required";
            }

            if (string.IsNullOrWhiteSpace(request.OutputFileName))
            {
                return "OutputFileName is required";
            }

            var htmlBytes = Encoding.UTF8.GetByteCount(request.HtmlContent);
            if (htmlBytes > MaxHtmlContentBytes)
            {
                return $"HtmlContent exceeds maximum size of {MaxHtmlContentBytes} bytes";
            }

            return null;
        }

        private static PdfGenerationOptions BuildPdfOptions(CreateSinglePdfFromHtmlRequest request)
        {
            return new PdfGenerationOptions
            {
                HeaderHtml = request.HeaderHtml,
                FooterHtml = request.FooterHtml,
                HeaderHeight = ParseHeight(request.HeaderHeight),
                FooterHeight = ParseHeight(request.FooterHeight),
                IsPageNumberEnabled = request.IsPageNumberEnabled,
                IsTotalPageCountEnabled = request.IsTotalPageCountEnabled,
                PageNumberText = request.PageNumberText
            };
        }

        private static double ParseHeight(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static string ResolveAccessModifier(string? accessModifier)
        {
            if (string.Equals(accessModifier, "Public", StringComparison.OrdinalIgnoreCase))
            {
                return "Public";
            }

            return "Private";
        }

        /// <summary>
        /// Races the in-process Puppeteer render against a wall-clock timeout.
        /// <see cref="PuppeteerSharpEngine.ConvertHtmlToPdfAsync"/> and
        /// <see cref="PuppeteerSharpEngine"/>'s browser launch are not cancellable, so a timeout
        /// here means this endpoint gives up and reports failure — the underlying Puppeteer call
        /// may still complete in the background against the shared browser singleton.
        /// </summary>
        private async Task<Stream?> RenderWithTimeoutAsync(string htmlContent, PdfGenerationOptions options)
        {
            var renderTask = _engine.ConvertHtmlToPdfAsync(htmlContent, options);
            var timeoutTask = Task.Delay(RenderTimeout);
            var completed = await Task.WhenAny(renderTask, timeoutTask);

            if (completed != renderTask)
            {
                // Do not await renderTask: that would wait out the hang this timeout exists to
                // bound. The engine swallows its own exceptions, so an unobserved fault is not
                // expected; the render may still finish later on the shared browser.
                throw new TimeoutException();
            }

            return await renderTask;
        }

        private static CreateSinglePdfFromHtmlResponse Fail(string message, string? correlationId)
        {
            return new CreateSinglePdfFromHtmlResponse
            {
                IsSuccess = false,
                Message = message,
                MessageCoRelationId = correlationId
            };
        }
    }
}
