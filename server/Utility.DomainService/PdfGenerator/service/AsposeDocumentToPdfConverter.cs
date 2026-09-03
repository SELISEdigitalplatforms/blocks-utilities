using Aspose.Words;
using Aspose.Words.Fonts;
using Aspose.Words.Saving;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Converts word-processing documents to PDF with Aspose.Words.
    /// </summary>
    /// <remarks>
    /// Aspose is the only library in this solution that reads the binary .doc format and Word's
    /// layout model, which is what makes a converted document paginate the way the author saw it.
    /// The alternative considered was rendering via HTML through the Puppeteer engine; that loses
    /// pagination, headers/footers and form fields, so it is not a substitute here.
    ///
    /// The type is a singleton and holds no per-conversion state: every <c>Document</c> is local to
    /// the call, so concurrent conversions do not interact.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class AsposeDocumentToPdfConverter : IDocumentToPdfConverter
    {
        /// <summary>
        /// Extensions Aspose.Words reads. Kept explicit rather than "anything that is not a PDF" so
        /// a caller that points at a spreadsheet or an image gets a clear rejection before the file
        /// is downloaded and handed to a parser that would fail on it in a less obvious way.
        /// </summary>
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm",
            ".rtf", ".odt", ".ott", ".txt", ".md", ".html", ".htm", ".mhtml"
        };

        /// <summary>
        /// Font directories searched in addition to the ones Aspose finds itself. A Linux container
        /// has no Word fonts, and Aspose silently substitutes a default face when a requested font
        /// is missing, which shifts line breaks and repaginates the document. Listing the image's
        /// own font locations lets the substitution at least land on a metric-compatible face.
        /// </summary>
        private static readonly string[] FontDirectories =
        {
            "/app/fonts",
            "/usr/share/fonts",
            "/usr/local/share/fonts"
        };

        private readonly ILogger<AsposeDocumentToPdfConverter> _logger;
        private readonly IConfiguration _configuration;
        private int _fontsConfigured;

        public AsposeDocumentToPdfConverter(
            ILogger<AsposeDocumentToPdfConverter> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <inheritdoc />
        public bool IsSupportedDocument(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            return SupportedExtensions.Contains(Path.GetExtension(fileName));
        }

        /// <inheritdoc />
        public async Task<Stream?> ConvertToPdfAsync(Stream documentStream, DocumentConversionOptions options)
        {
            ArgumentNullException.ThrowIfNull(documentStream);
            ArgumentNullException.ThrowIfNull(options);

            try
            {
                AsposeLicense.EnsureApplied(_configuration, _logger);
                ConfigureFontSources();

                // Aspose needs a seekable stream it can read repeatedly; a storage download stream
                // is typically neither. Buffering once here is cheaper than letting the parser fail
                // partway through a document it cannot rewind.
                using var buffered = new MemoryStream();
                if (documentStream.CanSeek)
                {
                    documentStream.Position = 0;
                }

                await documentStream.CopyToAsync(buffered);
                buffered.Position = 0;

                if (buffered.Length == 0)
                {
                    _logger.LogError("AsposeDocumentToPdfConverter: Source document is empty");
                    return null;
                }

                var document = new Document(buffered);

                // Aspose reports substituted fonts and unsupported features through this callback
                // rather than by failing, so without it a document that rendered wrongly is
                // indistinguishable from one that rendered correctly.
                var warnings = new WarningInfoCollection();
                document.WarningCallback = warnings;

                var saveOptions = BuildSaveOptions(options);

                var outputStream = new MemoryStream();
                document.Save(outputStream, saveOptions);
                outputStream.Position = 0;

                foreach (WarningInfo warning in warnings)
                {
                    _logger.LogWarning(
                        "AsposeDocumentToPdfConverter: {Source}/{Type} - {Description}",
                        warning.Source,
                        warning.WarningType,
                        warning.Description);
                }

                if (outputStream.Length == 0)
                {
                    _logger.LogError("AsposeDocumentToPdfConverter: Conversion produced an empty PDF");
                    await outputStream.DisposeAsync();
                    return null;
                }

                _logger.LogInformation(
                    "AsposeDocumentToPdfConverter: Converted document to PDF, pages={PageCount}, size={PdfSize} bytes, licensed={IsLicensed}",
                    document.PageCount,
                    outputStream.Length,
                    AsposeLicense.IsLicensed);

                return outputStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AsposeDocumentToPdfConverter: Error converting document to PDF");
                return null;
            }
        }

        private static PdfSaveOptions BuildSaveOptions(DocumentConversionOptions options)
        {
            var saveOptions = new PdfSaveOptions
            {
                SaveFormat = SaveFormat.Pdf,
                PreserveFormFields = options.PreserveFormFields,
                EmbedFullFonts = options.EmbedFullFonts,
                UpdateFields = options.UpdateFields,
                ImageCompression = options.CompressImages
                    ? PdfImageCompression.Jpeg
                    : PdfImageCompression.Auto
            };

            if (options.PdfACompliant)
            {
                saveOptions.Compliance = PdfCompliance.PdfA1b;

                // PDF/A requires every glyph the file can display to be embedded, and forbids the
                // form-field dictionaries PreserveFormFields would emit. Setting the compliance
                // level without these produces a file that claims PDF/A and fails validation.
                saveOptions.EmbedFullFonts = true;
                saveOptions.PreserveFormFields = false;
            }

            return saveOptions;
        }

        /// <summary>
        /// Adds this host's font directories to Aspose's search path, once per process.
        /// </summary>
        /// <remarks>
        /// <c>FontSettings.DefaultInstance</c> is process-global mutable state, so doing this per
        /// conversion would append the same sources repeatedly and race with a concurrent
        /// conversion reading them. The interlocked flag makes the first caller do it and everyone
        /// else skip.
        /// </remarks>
        private void ConfigureFontSources()
        {
            if (Interlocked.Exchange(ref _fontsConfigured, 1) == 1)
            {
                return;
            }

            try
            {
                var existing = FontSettings.DefaultInstance.GetFontsSources();
                var additional = FontDirectories
                    .Where(Directory.Exists)
                    .Select(directory => new FolderFontSource(directory, true))
                    .Cast<FontSourceBase>()
                    .ToArray();

                if (additional.Length == 0)
                {
                    _logger.LogInformation(
                        "AsposeDocumentToPdfConverter: No additional font directories found; using Aspose defaults");
                    return;
                }

                FontSettings.DefaultInstance.SetFontsSources(existing.Concat(additional).ToArray());

                _logger.LogInformation(
                    "AsposeDocumentToPdfConverter: Added {Count} font source(s): {Directories}",
                    additional.Length,
                    string.Join(", ", FontDirectories.Where(Directory.Exists)));
            }
            catch (Exception ex)
            {
                // Font discovery failing degrades output quality but does not stop a conversion, so
                // it must not stop one.
                _logger.LogWarning(ex, "AsposeDocumentToPdfConverter: Failed to configure font sources");
            }
        }
    }
}
