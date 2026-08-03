using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// PDF engine using WkHtmlToPdf for HTML to PDF conversion
    /// Requires wkhtmltopdf binary installed on the system
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class WkHtmlToPdfEngine : IPdfEngine
    {
        private readonly ILogger<WkHtmlToPdfEngine> _logger;
        private readonly IConfiguration _configuration;

        public WkHtmlToPdfEngine(ILogger<WkHtmlToPdfEngine> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<Stream?> MergePdfsAsync(List<Stream> pdfStreams)
        {
            _logger.LogWarning("WkHtmlToPdfEngine: MergePdfsAsync not supported. Use AsposePdfEngine for merge operations.");
            // WkHtmlToPdf doesn't support PDF merging, delegate to Aspose or use PdfSharp
            await Task.CompletedTask;
            return null;
        }

        public async Task<Stream?> ConvertHtmlToPdfAsync(string htmlContent, PdfGenerationOptions options)
        {
            try
            {
                _logger.LogInformation("WkHtmlToPdfEngine: Converting HTML to PDF using WkHtmlToPdf");
                
                var config = new Shark.PdfConvert.PdfConversionSettings
                {
                    Zoom = float.Parse(options.Profile?.Zoom ?? "0.8"),
                    PdfToolPath = _configuration["PdfToolPath"] ?? "/usr/bin/wkhtmltopdf",
                    PageWidth = float.Parse(options.Profile?.Width ?? "1200"),
                    PageHeight = float.Parse(options.Profile?.Height ?? "800"),
                    LowQuality = false,
                };
                
                // Add headers and footers
                if (!string.IsNullOrEmpty(options.HeaderHtml))
                {
                    _logger.LogInformation("WkHtmlToPdfEngine: Adding header HTML");
                    config.PageHeaderHtml = options.HeaderHtml;
                }
                
                if (!string.IsNullOrEmpty(options.FooterHtml))
                {
                    _logger.LogInformation("WkHtmlToPdfEngine: Adding footer HTML");
                    config.PageFooterHtml = options.FooterHtml;
                }
                
                var htmlBytes = Encoding.UTF8.GetBytes(htmlContent);
                var memoryStream = new MemoryStream();
                
                using (var htmlStream = new MemoryStream(htmlBytes))
                {
                    Shark.PdfConvert.PdfConvert.Convert(
                        config: config,
                        contentInputStream: htmlStream,
                        pdfOutputStream: memoryStream);
                }
                
                // Add page numbers if enabled
                if (options.IsPageNumberEnabled)
                {
                    memoryStream = AddPageNumbers(memoryStream);
                }
                
                memoryStream.Position = 0;
                _logger.LogInformation("WkHtmlToPdfEngine: Successfully converted HTML to PDF, size={Size} bytes", memoryStream.Length);
                return await Task.FromResult<Stream?>(memoryStream);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WkHtmlToPdfEngine: Error converting HTML to PDF");
                return null;
            }
        }
        
        private MemoryStream AddPageNumbers(MemoryStream memoryStream)
        {
            try
            {
                using (var document = PdfReader.Open(memoryStream))
                {
                    int pageCounter = 0;
                    
                    foreach (var page in document.Pages)
                    {
                        var gfx = XGraphics.FromPdfPage(page);
                        var font = new XFont("Calibri", 6, XFontStyleEx.Regular);

                        gfx.DrawString(
                            text: $"Page {++pageCounter} of {document.Pages.Count}",
                            font: font,
                            brush: XBrushes.Black,
                            layoutRectangle: new XRect(0, 0, page.Width.Point - 20, page.Height.Point - 20),
                            format: XStringFormats.BottomRight);
                    }
                    
                    var outputStream = new MemoryStream();
                    document.Save(outputStream);
                    outputStream.Position = 0;
                    return outputStream;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WkHtmlToPdfEngine: Error adding page numbers");
                return memoryStream;
            }
        }

        public async Task<string?> ExtractTextFromPdfAsync(Stream pdfStream)
        {
            _logger.LogWarning("WkHtmlToPdfEngine: ExtractTextFromPdfAsync not supported. Use AsposePdfEngine for text extraction.");
            // WkHtmlToPdf doesn't support text extraction
            await Task.CompletedTask;
            return null;
        }

        public async Task<Stream?> FixPdfAsync(Stream pdfStream)
        {
            _logger.LogWarning("WkHtmlToPdfEngine: FixPdfAsync not supported. Use AsposePdfEngine for PDF repair.");
            await Task.CompletedTask;
            return null;
        }

        public async Task<Stream?> StampImageToPdfAsync(Stream pdfStream, Stream imageStream, ImageStampOptions options)
        {
            _logger.LogWarning("WkHtmlToPdfEngine: StampImageToPdfAsync not supported. Use AsposePdfEngine for stamping.");
            await Task.CompletedTask;
            return null;
        }

        public async Task<Stream?> StampTextToPdfAsync(Stream pdfStream, TextStampOptions options)
        {
            _logger.LogWarning("WkHtmlToPdfEngine: StampTextToPdfAsync not supported. Use AsposePdfEngine for stamping.");
            await Task.CompletedTask;
            return null;
        }
    }
}

