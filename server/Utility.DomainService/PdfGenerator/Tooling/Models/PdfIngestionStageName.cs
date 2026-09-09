namespace Utility.DomainService.PdfGenerator.Tooling.Models;

public enum PdfIngestionStageName
{
    Inspect,
    QpdfNormalize,
    PdfBoxGeometryNormalize,
    VeraPdfValidate,
    PdfARepairFlatten,
    PdfARepairGhostscript,
    PdfARepairRevalidate,
    PdfARepairStandardFallback
}
