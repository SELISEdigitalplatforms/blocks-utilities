using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// PDF engine using PuppeteerSharp (headless Chromium) for HTML to PDF conversion.
    /// This engine provides excellent CSS3, JavaScript, and modern web rendering support.
    /// 
    /// Supported operations:
    /// - ConvertHtmlToPdfAsync: Excellent (uses Chrome's native print-to-PDF)
    /// 
    /// Unsupported operations (returns null):
    /// - MergePdfsAsync: Use AsposePdfEngine (Engine 2)
    /// - ExtractTextFromPdfAsync: Use AsposePdfEngine (Engine 2)
    /// - FixPdfAsync: Use AsposePdfEngine (Engine 2)
    /// - StampImageToPdfAsync: Use AsposePdfEngine (Engine 2)
    /// - StampTextToPdfAsync: Use AsposePdfEngine (Engine 2)
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class PuppeteerSharpEngine : IPdfEngine, IAsyncDisposable
    {
        private readonly ILogger<PuppeteerSharpEngine> _logger;
        private readonly IConfiguration _configuration;
        private IBrowser? _browser;
        private readonly SemaphoreSlim _browserLock = new(1, 1);
        private bool _disposed;

        public PuppeteerSharpEngine(
            ILogger<PuppeteerSharpEngine> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Gets or creates a shared browser instance for PDF generation.
        /// Uses lazy initialization with thread-safe access.
        /// </summary>
        private async Task<IBrowser> GetBrowserAsync()
        {
            if (_browser != null && _browser.IsConnected)
            {
                return _browser;
            }

            await _browserLock.WaitAsync();
            try
            {
                if (_browser != null && _browser.IsConnected)
                {
                    return _browser;
                }

                _logger.LogInformation("PuppeteerSharpEngine: Initializing browser instance");

                // Check for configured executable path or download Chromium
                var executablePath = _configuration["PuppeteerSharp:ExecutablePath"];
                
                if (string.IsNullOrEmpty(executablePath))
                {
                    _logger.LogInformation("PuppeteerSharpEngine: No executable path configured, downloading Chromium...");
                    var browserFetcher = new BrowserFetcher();
                    var installedBrowser = await browserFetcher.DownloadAsync();
                    executablePath = installedBrowser.GetExecutablePath();
                }

                var launchOptions = new LaunchOptions
                {
                    Headless = true,
                    ExecutablePath = executablePath,
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu",
                        "--disable-software-rasterizer",
                        "--font-render-hinting=none"
                    }
                };

                _browser = await Puppeteer.LaunchAsync(launchOptions);
                _logger.LogInformation("PuppeteerSharpEngine: Browser initialized successfully");

                return _browser;
            }
            finally
            {
                _browserLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<Stream?> ConvertHtmlToPdfAsync(string htmlContent, PdfGenerationOptions options)
        {
            try
            {
                _logger.LogInformation("PuppeteerSharpEngine: Converting HTML to PDF using headless Chromium");

                var browser = await GetBrowserAsync();
                await using var page = await browser.NewPageAsync();

                // Set page content
                await page.SetContentAsync(htmlContent, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle0 }
                });

                // Wait for any JavaScript rendering to complete
                await page.WaitForNetworkIdleAsync();

                // Build PDF options
                var pdfOptions = BuildPdfOptions(options);

                // Generate PDF
                var pdfBytes = await page.PdfDataAsync(pdfOptions);

                var outputStream = new MemoryStream(pdfBytes);
                outputStream.Position = 0;

                _logger.LogInformation("PuppeteerSharpEngine: Successfully converted HTML to PDF, size={Size} bytes", outputStream.Length);
                return outputStream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PuppeteerSharpEngine: Error converting HTML to PDF");
                return null;
            }
        }

        /// <summary>
        /// Builds PdfOptions from PdfGenerationOptions and PdfUtilityProfile
        /// </summary>
        private PdfOptions BuildPdfOptions(PdfGenerationOptions options)
        {
            var pdfOptions = new PdfOptions
            {
                PrintBackground = true,
                PreferCSSPageSize = false,
                Format = PaperFormat.A4
            };

            // Apply profile settings if available
            if (options.Profile != null)
            {
                var profile = options.Profile;

                // Set margins
                pdfOptions.MarginOptions = new MarginOptions
                {
                    Left = ParseMargin(profile.MarginLeft, "10mm"),
                    Right = ParseMargin(profile.MarginRight, "10mm"),
                    Top = ParseMargin(profile.HeaderSpacing, options.HeaderHeight > 0 ? $"{options.HeaderHeight}px" : "10mm"),
                    Bottom = ParseMargin(profile.FooterSpacing, options.FooterHeight > 0 ? $"{options.FooterHeight}px" : "10mm")
                };

                // Set page size if specified
                if (!string.IsNullOrEmpty(profile.Width) && !string.IsNullOrEmpty(profile.Height))
                {
                    pdfOptions.Width = profile.Width;
                    pdfOptions.Height = profile.Height;
                }

                // Set orientation
                pdfOptions.Landscape = profile.Orientation?.ToLowerInvariant() == "landscape";

                // Set scale/zoom
                if (!string.IsNullOrEmpty(profile.Zoom) && double.TryParse(profile.Zoom, out var zoom))
                {
                    pdfOptions.Scale = (decimal)Math.Max(0.1, Math.Min(2.0, zoom));
                }
            }
            else
            {
                // Default margins
                pdfOptions.MarginOptions = new MarginOptions
                {
                    Top = options.HeaderHeight > 0 ? $"{options.HeaderHeight}px" : "10mm",
                    Bottom = options.FooterHeight > 0 ? $"{options.FooterHeight}px" : "10mm",
                    Left = "10mm",
                    Right = "10mm"
                };
            }

            // Add header template
            if (!string.IsNullOrEmpty(options.HeaderHtml))
            {
                pdfOptions.DisplayHeaderFooter = true;
                pdfOptions.HeaderTemplate = WrapHeaderFooterHtml(options.HeaderHtml);
            }

            // Add footer template
            if (!string.IsNullOrEmpty(options.FooterHtml))
            {
                pdfOptions.DisplayHeaderFooter = true;
                pdfOptions.FooterTemplate = WrapHeaderFooterHtml(options.FooterHtml);
            }

            // Add page numbers if enabled
            if (options.IsPageNumberEnabled)
            {
                pdfOptions.DisplayHeaderFooter = true;
                
                var pageNumberText = options.PageNumberText ?? "Page <span class=\"pageNumber\"></span>";
                
                if (options.IsTotalPageCountEnabled)
                {
                    pageNumberText = $"Page <span class=\"pageNumber\"></span> of <span class=\"totalPages\"></span>";
                }

                // If no custom footer, use page number as footer
                if (string.IsNullOrEmpty(options.FooterHtml))
                {
                    pdfOptions.FooterTemplate = $@"
                        <div style=""font-size: 10px; text-align: right; width: 100%; padding-right: 20px;"">
                            {pageNumberText}
                        </div>";
                }
                else
                {
                    // Append page numbers to existing footer
                    pdfOptions.FooterTemplate = WrapHeaderFooterHtml(options.FooterHtml + 
                        $@"<div style=""font-size: 10px; text-align: right;"">{pageNumberText}</div>");
                }
            }

            // Ensure header template is set if footer is displayed
            if (pdfOptions.DisplayHeaderFooter && string.IsNullOrEmpty(pdfOptions.HeaderTemplate))
            {
                pdfOptions.HeaderTemplate = "<span></span>";
            }

            // Ensure footer template is set if header is displayed
            if (pdfOptions.DisplayHeaderFooter && string.IsNullOrEmpty(pdfOptions.FooterTemplate))
            {
                pdfOptions.FooterTemplate = "<span></span>";
            }

            return pdfOptions;
        }

        /// <summary>
        /// Wraps header/footer HTML with required styling for Puppeteer.
        /// Puppeteer requires specific styling for header/footer templates.
        /// </summary>
        private static string WrapHeaderFooterHtml(string html)
        {
            // Puppeteer header/footer templates must include font-size styling
            // as they render in a separate context with no default styling
            return $@"
                <style>
                    html, body {{ margin: 0; padding: 0; }}
                    * {{ font-size: 10px; font-family: Arial, sans-serif; }}
                </style>
                <div style=""width: 100%; padding: 0 20px;"">
                    {html}
                </div>";
        }

        /// <summary>
        /// Parses margin value, returning default if invalid
        /// </summary>
        private static string ParseMargin(string? value, string defaultValue)
        {
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            // If numeric only, assume pixels
            if (double.TryParse(value, out var numericValue))
            {
                return $"{numericValue}px";
            }

            return value;
        }

        #region Unsupported Operations

        /// <inheritdoc />
        public Task<Stream?> MergePdfsAsync(List<Stream> pdfStreams)
        {
            _logger.LogWarning("PuppeteerSharpEngine: MergePdfsAsync not supported. Use AsposePdfEngine (Engine 2) for merge operations.");
            return Task.FromResult<Stream?>(null);
        }

        /// <inheritdoc />
        public Task<string?> ExtractTextFromPdfAsync(Stream pdfStream)
        {
            _logger.LogWarning("PuppeteerSharpEngine: ExtractTextFromPdfAsync not supported. Use AsposePdfEngine (Engine 2) for text extraction.");
            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public Task<Stream?> FixPdfAsync(Stream pdfStream)
        {
            _logger.LogWarning("PuppeteerSharpEngine: FixPdfAsync not supported. Use AsposePdfEngine (Engine 2) for PDF repair.");
            return Task.FromResult<Stream?>(null);
        }

        /// <inheritdoc />
        public Task<Stream?> StampImageToPdfAsync(Stream pdfStream, Stream imageStream, ImageStampOptions options)
        {
            _logger.LogWarning("PuppeteerSharpEngine: StampImageToPdfAsync not supported. Use AsposePdfEngine (Engine 2) for stamping.");
            return Task.FromResult<Stream?>(null);
        }

        /// <inheritdoc />
        public Task<Stream?> StampTextToPdfAsync(Stream pdfStream, TextStampOptions options)
        {
            _logger.LogWarning("PuppeteerSharpEngine: StampTextToPdfAsync not supported. Use AsposePdfEngine (Engine 2) for stamping.");
            return Task.FromResult<Stream?>(null);
        }

        #endregion

        #region IAsyncDisposable

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_browser != null)
            {
                _logger.LogInformation("PuppeteerSharpEngine: Disposing browser instance");
                await _browser.CloseAsync();
                await _browser.DisposeAsync();
                _browser = null;
            }

            _browserLock.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}


