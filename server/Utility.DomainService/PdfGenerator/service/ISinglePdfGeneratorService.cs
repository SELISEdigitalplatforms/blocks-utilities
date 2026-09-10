namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Synchronous single-file HTML-to-PDF generation. Separate from
    /// <see cref="IPdfGeneratorService"/> so that interface is not grown further.
    /// </summary>
    public interface ISinglePdfGeneratorService
    {
        Task<CreateSinglePdfFromHtmlResponse> CreateSinglePdfFromHtmlAsync(CreateSinglePdfFromHtmlRequest request);
    }
}
