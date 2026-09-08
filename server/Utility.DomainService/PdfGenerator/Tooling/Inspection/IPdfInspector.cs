namespace Utility.DomainService.PdfGenerator.Tooling.Inspection;

public interface IPdfInspector
{
    /// <summary>
    /// Synchronous: both underlying libraries parse the given bytes entirely in memory, so there is
    /// no I/O to make asynchronous.
    /// </summary>
    PdfInspectionResult Inspect(byte[] pdfBytes);
}
