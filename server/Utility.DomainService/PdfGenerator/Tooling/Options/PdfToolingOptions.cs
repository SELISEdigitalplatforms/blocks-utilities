namespace Utility.DomainService.PdfGenerator.Tooling.Options;

/// <summary>
/// Ported from l3-net-signature-pdfengine's <c>PdfEngineOptions</c>, minus every storage/identity
/// key (<see cref="Utility.DomainService.PdfGenerator.service.PdfStorageHelper"/> owns that here)
/// and <c>StoreRawVeraPdfReportByDefault</c>, since the raw veraPDF report is never persisted.
/// </summary>
public sealed class PdfToolingOptions
{
    public const string SectionName = "PdfIngestion";

    public string TempDirectory { get; set; } = "/tmp/pdf-ingestion";

    public QpdfExecutionMode QpdfExecutionMode { get; set; } = QpdfExecutionMode.Direct;
    public string QpdfPath { get; set; } = "/usr/bin/qpdf";
    public string QpdfDockerImage { get; set; } = "blocks-qpdf:dev";

    public string DockerPath { get; set; } = "docker";
    public string DockerContainerWorkspacePath { get; set; } = "/work";

    public VeraPdfExecutionMode VeraPdfExecutionMode { get; set; } = VeraPdfExecutionMode.Direct;
    public string VeraPdfPath { get; set; } = "/opt/verapdf/verapdf";
    public string VeraPdfDockerImage { get; set; } = "ghcr.io/verapdf/cli:latest";

    public PdfBoxExecutionMode PdfBoxExecutionMode { get; set; } = PdfBoxExecutionMode.Direct;
    public string PdfBoxDockerImage { get; set; } = "blocks-pdfbox:dev";
    public string JavaPath { get; set; } = "java";
    public string PdfBoxJarPath { get; set; } = "/opt/pdfbox/pdfbox-app.jar";
    public string PdfBoxClassPath { get; set; } = "/opt/pdfbox";

    public string GhostscriptPath { get; set; } = "/usr/bin/gs";
    public string GhostscriptPdfADefinitionPath { get; set; } = "/opt/pdfingestion/PDFA_def.ps";

    /// <summary>
    /// Alpine installs ICC profiles under <c>/usr/share/ghostscript/&lt;version&gt;/iccprofiles/</c>,
    /// not Debian's <c>/usr/share/color/icc/ghostscript/</c> that <c>GhostscriptCommandFactory</c> and
    /// <c>PDFA_def.ps</c> were both written against. The image symlinks Alpine's real profile to the
    /// Debian-shaped path so <c>PDFA_def.ps</c> can stay byte-identical to upstream; this option lets
    /// the argument follow config instead of repeating the literal path a second time in code.
    /// </summary>
    public string GhostscriptIccProfilePath { get; set; } = "/usr/share/color/icc/ghostscript/srgb.icc";

    public int DefaultTimeoutSeconds { get; set; } = 60;
    public int MaxFileSizeMb { get; set; } = 50;

    /// <summary>
    /// Ghostscript and a JVM (PDFBox) may run alongside the Worker's resident Chromium instance,
    /// so the default is deliberately lower than PdfEngine's standalone default of 4.
    /// </summary>
    public int MaxParallelExternalProcesses { get; set; } = 2;
}
