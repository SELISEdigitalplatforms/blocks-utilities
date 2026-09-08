namespace Utility.DomainService.PdfGenerator.Tooling;

public static class PdfToolErrorCodes
{
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string QpdfFailed = "QPDF_FAILED";
    public const string VeraPdfFailed = "VERAPDF_FAILED";
    public const string PdfBoxFailed = "PDFBOX_FAILED";
    public const string PdfBoxGeometryFailed = "PDFBOX_GEOMETRY_FAILED";
    public const string SignedDocument = "SIGNED_DOCUMENT";
    public const string GeometryReportUnreadable = "GEOMETRY_REPORT_UNREADABLE";
    public const string GhostscriptFailed = "GHOSTSCRIPT_FAILED";
    public const string StandardPdfConversionFailed = "STANDARD_PDF_CONVERSION_FAILED";
    public const string ProcessTimeout = "PROCESS_TIMEOUT";
    public const string OutputNotCreated = "OUTPUT_NOT_CREATED";
    public const string InternalError = "INTERNAL_ERROR";
}
