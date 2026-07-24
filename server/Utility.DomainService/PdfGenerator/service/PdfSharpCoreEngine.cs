using Microsoft.Extensions.Logging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// PDF engine using PdfSharp for PDF manipulation operations.
    /// This is a free, pure .NET library (MIT license) for PDF manipulation.
    ///
    /// Supported operations:
    /// - MergePdfsAsync: Native support using PdfSharp
    /// - FixPdfAsync: Open and resave to repair corrupted PDFs
    /// - StampImageToPdfAsync: Add images to PDF pages using XGraphics
    /// - StampTextToPdfAsync: Add plain text to PDF pages using XGraphics/XFont
    ///
    /// Unsupported operations (returns null):
    /// - ConvertHtmlToPdfAsync: PdfSharp has no HTML rendering engine. Use Engine 1 (PuppeteerSharp).
    /// - ExtractTextFromPdfAsync: Limited support. Use Engine 3 (Aspose) for reliable text extraction.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PdfSharpCoreEngine : IPdfEngine
    {
        private readonly ILogger<PdfSharpCoreEngine> _logger;

        public PdfSharpCoreEngine(ILogger<PdfSharpCoreEngine> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<Stream?> MergePdfsAsync(List<Stream> pdfStreams)
        {
            try
            {
                _logger.LogInformation("PdfSharpCoreEngine: Merging {PdfCount} PDF files", pdfStreams.Count);

                if (pdfStreams == null || pdfStreams.Count == 0)
                {
                    _logger.LogError("PdfSharpCoreEngine: No PDF streams to merge");
                    return null;
                }

                if (pdfStreams.Count == 1)
                {
                    _logger.LogInformation("PdfSharpCoreEngine: Only one PDF, returning as-is");
                    var singlePdfStream = new MemoryStream();
                    pdfStreams[0].Position = 0;
                    await pdfStreams[0].CopyToAsync(singlePdfStream);
                    singlePdfStream.Position = 0;
                    return singlePdfStream;
                }

                // Create output document
                using var outputDocument = new PdfDocument();

                foreach (var pdfStream in pdfStreams)
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        pdfStream.Position = 0;
                        await pdfStream.CopyToAsync(ms);
                        ms.Position = 0;

                        using var inputDocument = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
                        
                        for (int i = 0; i < inputDocument.PageCount; i++)
                        {
                            outputDocument.AddPage(inputDocument.Pages[i]);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "PdfSharpCoreEngine: Failed to add PDF to merge, skipping");
                        // Continue with other PDFs
                    }
                }

                if (outputDocument.PageCount == 0)
                {
                    _logger.LogError("PdfSharpCoreEngine: No pages were merged");
                    return null;
                }

                _logger.LogInformation("PdfSharpCoreEngine: Merged PDF has {PageCount} pages", outputDocument.PageCount);

                var outputStream = new MemoryStream();
                outputDocument.Save(outputStream);
                outputStream.Position = 0;

                return outputStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PdfSharpCoreEngine: Error merging PDFs");
                return null;
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// PdfSharpCore does NOT support HTML to PDF conversion.
        /// Use Engine 1 (PuppeteerSharp) for HTML to PDF with modern CSS/JS support.
        /// Use Engine 3 (Aspose) for HTML to PDF with legacy support.
        /// </remarks>
        public Task<Stream?> ConvertHtmlToPdfAsync(string htmlContent, PdfGenerationOptions options)
        {
            _logger.LogWarning("PdfSharpCoreEngine: ConvertHtmlToPdfAsync not supported. PdfSharpCore has no HTML rendering engine. Use Engine 1 (PuppeteerSharp) or Engine 3 (Aspose).");
            return Task.FromResult<Stream?>(null);
        }

        /// <inheritdoc />
        /// <remarks>
        /// PdfSharpCore has very limited text extraction capabilities.
        /// Use Engine 3 (Aspose) for reliable text extraction from PDFs.
        /// </remarks>
        public Task<string?> ExtractTextFromPdfAsync(Stream pdfStream)
        {
            _logger.LogWarning("PdfSharpCoreEngine: ExtractTextFromPdfAsync not supported. PdfSharpCore has limited text extraction. Use Engine 3 (Aspose).");
            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public async Task<Stream?> FixPdfAsync(Stream pdfStream)
        {
            try
            {
                _logger.LogInformation("PdfSharpCoreEngine: Fixing/repairing PDF by opening and resaving");

                using var ms = new MemoryStream();
                pdfStream.Position = 0;
                await pdfStream.CopyToAsync(ms);
                ms.Position = 0;

                // Open in modify mode and resave - this can fix some PDF issues
                using var document = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

                var outputStream = new MemoryStream();
                document.Save(outputStream);
                outputStream.Position = 0;

                _logger.LogInformation("PdfSharpCoreEngine: Successfully repaired PDF with {PageCount} pages, size={Size} bytes", document.PageCount, outputStream.Length);
                return outputStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PdfSharpCoreEngine: Error fixing PDF");
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<Stream?> StampImageToPdfAsync(Stream pdfStream, Stream imageStream, ImageStampOptions options)
        {
            try
            {
                _logger.LogInformation("PdfSharpCoreEngine: Stamping image at ({XPosition}, {YPosition})", options.XPosition, options.YPosition);

                using var pdfMs = new MemoryStream();
                pdfStream.Position = 0;
                await pdfStream.CopyToAsync(pdfMs);
                pdfMs.Position = 0;

                using var document = PdfReader.Open(pdfMs, PdfDocumentOpenMode.Modify);

                var pageNumbers = options.PageNumbers ?? Enumerable.Range(1, document.PageCount).ToList();

                // Read image bytes
                imageStream.Position = 0;
                using var imageMs = new MemoryStream();
                await imageStream.CopyToAsync(imageMs);
                var imageBytes = imageMs.ToArray();

                foreach (var pageNumber in pageNumbers)
                {
                    if (pageNumber <= 0 || pageNumber > document.PageCount)
                    {
                    _logger.LogWarning("PdfSharpCoreEngine: Page number {PageNumber} out of range, skipping", pageNumber);
                        continue;
                    }

                    var page = document.Pages[pageNumber - 1];
                    using var gfx = XGraphics.FromPdfPage(page);

                    DrawImage(
                        gfx: gfx,
                        imageBytes: imageBytes,
                        x: (float)options.XPosition,
                        y: (float)options.YPosition,
                        width: (float)options.Width,
                        height: (float)options.Height);
                }

                var outputStream = new MemoryStream();
                document.Save(outputStream);
                outputStream.Position = 0;

                _logger.LogInformation("PdfSharpCoreEngine: Successfully stamped image, size={Size} bytes", outputStream.Length);
                return outputStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PdfSharpCoreEngine: Error stamping image to PDF");
                return null;
            }
        }

        private static void DrawImage(XGraphics gfx, byte[] imageBytes, float x, float y, float width, float height)
        {
            using var imageStream = new MemoryStream(imageBytes);
            using var image = XImage.FromStream(imageStream);

            double ratioWidth = width / image.PixelWidth;
            double ratioHeight = height / image.PixelHeight;
            double ratio = Math.Min(ratioWidth, ratioHeight);

            var xOffset = (width - (image.PixelWidth * ratio)) / 2;
            var yOffset = (height - (image.PixelHeight * ratio)) / 2;

            // Apply coordinate scaling (0.75 factor for point conversion)
            var finalX = (x + xOffset) * 0.75;
            var finalY = (y + yOffset) * 0.75;
            var finalWidth = image.PixelWidth * ratio * 0.75;
            var finalHeight = image.PixelHeight * ratio * 0.75;

            gfx.DrawImage(image, finalX, finalY, finalWidth, finalHeight);
        }

        /// <inheritdoc />
        public async Task<Stream?> StampTextToPdfAsync(Stream pdfStream, TextStampOptions options)
        {
            try
            {
                _logger.LogInformation("PdfSharpCoreEngine: Stamping text '{Text}' at ({XPosition}, {YPosition})", options.Text, options.XPosition, options.YPosition);

                using var pdfMs = new MemoryStream();
                pdfStream.Position = 0;
                await pdfStream.CopyToAsync(pdfMs);
                pdfMs.Position = 0;

                using var document = PdfReader.Open(pdfMs, PdfDocumentOpenMode.Modify);

                var pageNumbers = options.PageNumbers ?? Enumerable.Range(1, document.PageCount).ToList();

                foreach (var pageNumber in pageNumbers)
                {
                    if (pageNumber <= 0 || pageNumber > document.PageCount)
                    {
                    _logger.LogWarning("PdfSharpCoreEngine: Page number {PageNumber} out of range, skipping", pageNumber);
                        continue;
                    }

                    var page = document.Pages[pageNumber - 1];
                    using var gfx = XGraphics.FromPdfPage(page);

                    DrawText(
                        gfx: gfx,
                        text: options.Text,
                        fontName: options.FontName ?? "Calibri",
                        fontSize: options.FontSize > 0 ? options.FontSize : 12,
                        x: (float)options.XPosition,
                        y: (float)options.YPosition,
                        isBold: options.IsBold,
                        isItalic: options.IsItalic);
                }

                var outputStream = new MemoryStream();
                document.Save(outputStream);
                outputStream.Position = 0;

                _logger.LogInformation("PdfSharpCoreEngine: Successfully stamped text, size={Size} bytes", outputStream.Length);
                return outputStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PdfSharpCoreEngine: Error stamping text to PDF");
                return null;
            }
        }

        private static void DrawText(XGraphics gfx, string text, string fontName, double fontSize, float x, float y, bool isBold, bool isItalic)
        {
            // Render the stamp as plain text directly onto the page using XFont.
            // PdfSharp 6 has no HTML rendering engine (HtmlRendererCore was PdfSharpCore-only),
            // so HTML-formatted stamps are no longer supported and text is drawn as-is.
            var fontStyle = XFontStyleEx.Regular;
            if (isBold && isItalic) fontStyle = XFontStyleEx.BoldItalic;
            else if (isBold) fontStyle = XFontStyleEx.Bold;
            else if (isItalic) fontStyle = XFontStyleEx.Italic;

            var font = new XFont(fontName, fontSize, fontStyle);
            var brush = XBrushes.Black;

            gfx.DrawString(text, font, brush, new XPoint(x * 0.75, y * 0.75));
        }
    }
}


