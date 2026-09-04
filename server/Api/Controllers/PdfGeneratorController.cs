using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Utility.DomainService.PdfGenerator.service;
using Utility.DomainService.PdfGenerator;

namespace Api.Controllers
{
    /// <summary>
    /// API Controller for managing PDF generator operations.
    /// 
    /// Available PDF Engines:
    /// - Engine 1 (PuppeteerSharp): Default. Best for HTML-to-PDF with modern CSS3/JavaScript support. Free (MIT license).
    /// - Engine 2 (PdfSharpCore): PDF manipulation only (merge, stamp, fix). Free (MIT license). NO HTML-to-PDF support.
    /// - Engine 3 (Aspose): Full PDF support including HTML-to-PDF, merge, stamp, extract text, and fix. Commercial license.
    /// - Engine 4 (WkHtmlToPdf): HTML-to-PDF only. Free but deprecated.
    /// 
    /// Recommended usage:
    /// - HTML-to-PDF: Use Engine 1 (PuppeteerSharp) for modern CSS/JS, Engine 3 (Aspose) or 4 (WkHtml) for legacy.
    /// - PDF manipulation (merge, stamp, fix): Use Engine 2 (PdfSharpCore) for free, Engine 3 (Aspose) for full features.
    /// - Text extraction: Use Engine 3 (Aspose) only.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class PdfGeneratorController : ControllerBase
    {
        private readonly IPdfGeneratorService _pdfGeneratorService;

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfGeneratorController"/> class.
        /// </summary>
        /// <param name="pdfGeneratorService">The service used for PDF generator operations.</param>
        public PdfGeneratorController(
            IPdfGeneratorService pdfGeneratorService)
        {
            _pdfGeneratorService = pdfGeneratorService;
        }

        /// <summary>
        /// Merge multiple PDFs into single PDF file
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// OutputPdfFileId: PDF file ID of the new PDF file
        /// OutputPdfFileName: Name of the new PDF file
        /// MessageCoRelationId: Unique identifier for specific request
        /// Engine: PDF engine to use. 2=PdfSharpCore (free), 3=Aspose. Engine 1 (PuppeteerSharp) returns null (no merge support).
        /// PdfFilesToBeMerged: List of PDF files to be merged
        /// OpenInBrowser: Download Url directly opens the pdf in the browser instead of downloading
        /// HandleCorruptedPdf: If the pdf is corrupted it is resaved then further processing is done
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>MergePdfsResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<MergePdfsResponse> MergePdfs([FromBody] MergePdfsRequest request)
        {
            return await _pdfGeneratorService.MergePdfsAsync(request);
        }

        /// <summary>
        /// Export a webpage to PDF
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// MessageCoRelationId: Unique identifier for specific request
        /// EventReferenceData: Set of values stored on data fields related to a specific event
        /// CreateFromHtmlCommands: Create PDF file from an HTML file
        /// Engine: PDF engine to use. 1=PuppeteerSharp (default, best for modern CSS/JS), 3=Aspose, 4=WkHtmlToPdf (deprecated). Engine 2 (PdfSharpCore) returns null.
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>CreatePdfsFromHtmlResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<CreatePdfsFromHtmlResponse> CreatePdfsFromHtml([FromBody] CreatePdfsFromHtmlRequest request)
        {
            return await _pdfGeneratorService.CreatePdfsFromHtmlAsync(request);
        }

        /// <summary>
        /// Export a webpage to PDF using template engine
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// MessageCoRelationId: Unique identifier for specific request
        /// EventReferenceData: Set of values stored on data fields related to a specific event
        /// CreateFromHtmlCommands: Create PDF file from an HTML template
        /// Engine: PDF engine to use. 1=PuppeteerSharp (default, best for modern CSS/JS), 3=Aspose, 4=WkHtmlToPdf (deprecated). Engine 2 (PdfSharpCore) returns null.
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>CreatePdfsFromHtmlUsingTEResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<CreatePdfsFromHtmlUsingTEResponse> CreatePdfsFromHtmlUsingTemplateEngine([FromBody] CreatePdfsFromHtmlUsingTERequest request)
        {
            return await _pdfGeneratorService.CreatePdfsFromHtmlUsingTEAsync(request);
        }

        /// <summary>
        /// Export multiple webpages to PDF using template engine in bulk
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Bulk request with multiple payloads
        /// 
        /// MessageCoRelationId: Unique identifier for specific request
        /// CreateFromHtmlCommands: Create PDF files from HTML templates
        /// RaiseEventOnProcessEnding: Whether to raise event when processing completes
        /// NotifyOnProcessEnding: Whether to notify when processing completes
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>CreatePdfsFromHtmlUsingTEBulkResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<CreatePdfsFromHtmlUsingTEBulkResponse> CreatePdfsFromHtmlUsingTemplateEngineBulk([FromBody] CreatePdfsFromHtmlUsingTEBulkRequest request)
        {
            return await _pdfGeneratorService.CreatePdfsFromHtmlUsingTEBulkAsync(request);
        }

        /// <summary>
        /// Fixes existing pdf files
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// MessageCorrelationId: Unique identifier for specific request
        /// PdfInfos: Set of values indicating the original pdf files to be fixed and the output file to be generated
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>FixPdfsResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<FixPdfsResponse> FixPdfs([FromBody] FixPdfsRequest request)
        {
            return await _pdfGeneratorService.FixPdfsAsync(request);
        }

        /// <summary>
        /// Add an image within PDF file
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// PdfFileId: File ID of PDF
        /// OutputPdfFileId: File ID of the generated PDF after image has been added
        /// OutputPdfFileName: Name of the updated PDF file
        /// MessageCoRelationId: Unique identifier for specific request
        /// Stamps: List of all images added to PDF
        /// Engine: PDF engine to use. 2=PdfSharpCore (free), 3=Aspose. Engines 1 and 4 return null (no stamp support).
        /// EventReferenceData: Set of values stored on data fields related to a specific event
        /// OpenInBrowser: Download Url directly opens the pdf in the browser instead of downloading
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>StampImageToPdfResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<StampImageToPdfResponse> StampImageToPdf([FromBody] StampImageToPdfRequest request)
        {
            return await _pdfGeneratorService.StampImageToPdfAsync(request);
        }

        /// <summary>
        /// Add text stamps to PDF file
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// PdfFileId: File ID of PDF
        /// OutputPdfFileId: File ID of the generated PDF after text has been added
        /// Stamps: List of all text stamps with coordinates and font information
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>StampTextToPdfResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<StampTextToPdfResponse> StampTextToPdf([FromBody] StampTextToPdfRequest request)
        {
            return await _pdfGeneratorService.StampTextToPdfAsync(request);
        }

        /// <summary>
        /// Add both images and text stamps to PDF file
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        /// 
        /// PdfFileId: File ID of PDF
        /// OutputPdfFileId: File ID of the generated PDF
        /// Stamps: List of stamps (Type: 0=Image, 1=Text)
        /// ProjectKey: Project key for multi-tenancy
        /// </remarks>
        /// <returns>StampIntoPdfResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<StampIntoPdfResponse> StampIntoPdf([FromBody] StampIntoPdfRequest request)
        {
            return await _pdfGeneratorService.StampIntoPdfAsync(request);
        }

        /// <summary>
        /// Convert word-processing documents (.doc, .docx, .rtf, .odt, ...) to PDF, in place
        /// </summary>
        /// <remarks>
        /// <param name="request"></param> Request parameters:
        ///
        /// ConvertCommands: Documents to convert. Each command needs only:
        ///   DocumentFileId: The document in storage. The PDF is written back to this same ID and
        ///     the file is renamed to a .pdf extension, so the original document is replaced and
        ///     anything already referencing the ID ends up pointing at the PDF.
        /// Optional per command:
        ///   PreserveFormFields: Keep interactive form fields editable instead of flattening them.
        ///   PdfACompliant: Emit PDF/A-1b for archival. Forces font embedding and flattens fields.
        ///   UpdateFields: Recalculate page numbers, cross-references and TOC entries first.
        ///
        /// Optional on the request:
        ///   MessageCoRelationId: Identifies the request so the caller is notified when the batch
        ///     finishes. Omit it and the conversion still runs, without the completion notification.
        ///   EventReferenceData: Echoed back with the completion event.
        ///   ProjectKey: Defaults to the tenant on the ambient context. Supply it only for a call
        ///     made on behalf of another project.
        ///
        /// The document's name, extension and directory are read from its storage record, so they
        /// are never sent. Conversion uses Aspose.Words and takes no Engine number: it is the only
        /// library here that reads Word formats.
        /// </remarks>
        /// <returns>ConvertDocumentsToPdfResponse</returns>
        [HttpPost]
        [Authorize]
        public async Task<ConvertDocumentsToPdfResponse> ConvertDocumentsToPdf([FromBody] ConvertDocumentsToPdfRequest request)
        {
            return await _pdfGeneratorService.ConvertDocumentsToPdfAsync(request);
        }
    }
}
