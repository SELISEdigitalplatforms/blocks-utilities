namespace Utility.DomainService.PdfIngestion;

/// <summary>
/// The states an ingestion job moves through. Deliberately coarser than the verdict it produces:
/// a file can complete with an incomplete or non-compliant result (geometry still unreadable, a
/// PDF/A repair that fell back to a standard PDF) and still be <see cref="Completed"/> - this
/// tracks the job's own lifecycle, not how good the outcome was. <see cref="Failed"/> is reserved
/// for the job itself never producing a verdict (an unhandled exception, a crashed worker).
/// </summary>
public enum PdfIngestionStatus
{
    /// <summary>Accepted and waiting for a worker.</summary>
    Queued,

    /// <summary>A worker has picked it up.</summary>
    Processing,

    /// <summary>The pipeline ran and produced a verdict, whatever that verdict says.</summary>
    Completed,

    /// <summary>The job itself never produced a verdict. <c>ErrorCode</c> says why.</summary>
    Failed
}
