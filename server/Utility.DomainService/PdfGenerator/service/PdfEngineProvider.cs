using Microsoft.Extensions.Logging;

namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// PDF engine factory/provider service.
    /// 
    /// Available engines:
    /// - Engine 1: PuppeteerSharp (default) - Best for HTML-to-PDF with modern CSS (JavaScript is off). Free (MIT license).
    /// - Engine 2: PdfSharpCore - PDF manipulation (merge, stamp, fix). Free (MIT license). NO HTML-to-PDF support.
    /// - Engine 3: Aspose - Full PDF support (HTML-to-PDF, merge, stamp, extract, fix). Commercial license.
    /// - Engine 4 (WkHtmlToPdf) is turned off: it ran scripts in caller HTML and is no longer maintained.
    /// 
    /// Recommended usage:
    /// - HTML-to-PDF: Use Engine 1 (PuppeteerSharp) for modern CSS, Engine 3 (Aspose) for legacy.
    /// - PDF manipulation (merge, stamp): Use Engine 2 (PdfSharpCore) for free, Engine 3 (Aspose) for full features.
    /// - Text extraction: Use Engine 3 (Aspose) only.
    /// </summary>
    public interface IPdfEngineProvider
    {
        IPdfEngine GetEngine(int engineNumber);
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

    public class PdfEngineProvider : IPdfEngineProvider
    {
        private readonly PuppeteerSharpEngine _puppeteerEngine;
        private readonly PdfSharpCoreEngine _pdfSharpCoreEngine;
        private readonly AsposePdfEngine _asposeEngine;

        public PdfEngineProvider(
            PuppeteerSharpEngine puppeteerEngine,
            PdfSharpCoreEngine pdfSharpCoreEngine,
            AsposePdfEngine asposeEngine)
        {
            _puppeteerEngine = puppeteerEngine;
            _pdfSharpCoreEngine = pdfSharpCoreEngine;
            _asposeEngine = asposeEngine;
        }

        public IPdfEngine GetEngine(int engineNumber)
        {
            return engineNumber switch
            {
                1 => _puppeteerEngine,
                2 => _pdfSharpCoreEngine,
                3 => _asposeEngine,
                4 => throw new ArgumentException("Engine 4 (WkHtmlToPdf) is turned off. Use 1 (PuppeteerSharp) or 3 (Aspose)."),
                _ => throw new ArgumentException($"Invalid engine number: {engineNumber}. Supported values are 1 (PuppeteerSharp), 2 (PdfSharpCore) or 3 (Aspose).")
            };
        }
    }
}


