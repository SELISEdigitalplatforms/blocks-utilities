namespace Utility.DomainService.PdfGenerator.Events
{
    /// <summary>
    /// Event for merging PDFs
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record MergePdfsEvent
    {
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileName { get; set; } = string.Empty;
        public string MessageCoRelationId { get; set; } = string.Empty;
        public int Engine { get; set; }
        public List<PdfFileToBeMerged> PdfFilesToBeMerged { get; set; } = new();
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public bool OpenInBrowser { get; set; } = false;
        public bool HandleCorruptedPdf { get; set; } = false;
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Event for creating PDFs from HTML
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record CreatePdfsFromHtmlEvent
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<CreateFromHtmlCommand> CreateFromHtmlCommands { get; set; } = new();
        public int Engine { get; set; }
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Event for extracting text from PDFs
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record ExtractTextFromPdfsEvent
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public int Engine { get; set; }
        public List<ExtractTextCommand> ExtractTextCommands { get; set; } = new();
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Event for creating PDFs from HTML using template engine
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record CreatePdfsFromHtmlUsingTEEvent
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<CreateFromHtmlUsingTECommand> CreateFromHtmlCommands { get; set; } = new();
        public int Engine { get; set; }
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Event for creating PDFs from HTML using template engine in bulk
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record CreatePdfsFromHtmlUsingTEBulkEvent
    {
        public string MessageCoRelationId { get; set; } = string.Empty;
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public List<CreateFromHtmlUsingTEForBulkCommand> CreateFromHtmlCommands { get; set; } = new();
        public bool RaiseEventOnProcessEnding { get; set; } = true;
        public bool NotifyOnProcessEnding { get; set; } = false;
        public int Engine { get; set; }
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Event for fixing PDFs
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record FixPdfsEvent
    {
        public string MessageCorrelationId { get; set; } = string.Empty;
        public List<FixPdfCommand> PdfInfos { get; set; } = new();
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Event for stamping images to PDF
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record StampImageToPdfEvent
    {
        public string PdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileName { get; set; } = string.Empty;
        public string MessageCoRelationId { get; set; } = string.Empty;
        public List<Stamp> Stamps { get; set; } = new();
        public int Engine { get; set; }
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public bool OpenInBrowser { get; set; } = false;
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Event for stamping text to PDF
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record StampTextToPdfEvent
    {
        public string PdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileName { get; set; } = string.Empty;
        public string MessageCoRelationId { get; set; } = string.Empty;
        public List<StampText> Stamps { get; set; } = new();
        public int Engine { get; set; }
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public bool OpenInBrowser { get; set; } = false;
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Event for stamping into PDF
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record StampIntoPdfEvent
    {
        public string PdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileId { get; set; } = string.Empty;
        public string OutputPdfFileName { get; set; } = string.Empty;
        public string MessageCoRelationId { get; set; } = string.Empty;
        public List<StampInfo> Stamps { get; set; } = new();
        public int Engine { get; set; }
        public Dictionary<string, string>? EventReferenceData { get; set; }
        public bool OpenInBrowser { get; set; } = false;
        public string? ProjectKey { get; set; }
    }

    /// <summary>
    /// Event for converting one word-processing document to PDF
    /// </summary>
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public record ConvertDocumentToPdfEvent
    {
        /// <summary>
        /// The conversion record this event belongs to. The worker updates that record as it goes,
        /// which is what the status endpoint reads.
        /// </summary>
        public string ConversionId { get; set; } = string.Empty;

        public string InputFileId { get; set; } = string.Empty;
        public string? MessageCoRelationId { get; set; }
        public string? ProjectKey { get; set; }
    }
}
