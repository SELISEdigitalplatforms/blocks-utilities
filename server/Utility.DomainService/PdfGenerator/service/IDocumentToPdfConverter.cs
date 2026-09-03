namespace Utility.DomainService.PdfGenerator.service
{
    /// <summary>
    /// Converts word-processing documents (.doc, .docx, .rtf, .odt, ...) to PDF.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of <see cref="IPdfEngine"/>. Three of the four engines have no
    /// word-processing format support at all, so folding this in would mean three more methods that
    /// log a warning and return null, and would let a caller pick an engine number that silently
    /// cannot do the job. A caller that wants a document converted asks for a converter.
    /// </remarks>
    public interface IDocumentToPdfConverter
    {
        /// <summary>
        /// Reads a word-processing document from <paramref name="documentStream"/> and returns it as
        /// a PDF stream positioned at zero, or null if the conversion failed.
        /// </summary>
        Task<Stream?> ConvertToPdfAsync(Stream documentStream, DocumentConversionOptions options);

        /// <summary>
        /// True when <paramref name="fileName"/> has an extension this converter can read.
        /// </summary>
        bool IsSupportedDocument(string fileName);
    }

    /// <summary>
    /// Options controlling how a document is rendered to PDF.
    /// </summary>
    public class DocumentConversionOptions
    {
        /// <summary>
        /// Keeps interactive form fields as PDF form fields instead of flattening them to static
        /// text. Off by default: a converted document is normally an archival or signing artefact,
        /// where a reader being able to edit the fields is a hazard rather than a feature.
        /// </summary>
        public bool PreserveFormFields { get; set; }

        /// <summary>
        /// Embeds complete font files rather than the used-glyphs subset. Larger output, but the
        /// only way a downstream tool that re-renders or edits the PDF gets the whole typeface.
        /// </summary>
        public bool EmbedFullFonts { get; set; } = true;

        /// <summary>
        /// Recalculates fields (page counts, cross-references, TOC entries) before rendering.
        /// Off by default, matching the platform's existing conversion behaviour: a document whose
        /// fields reference an external source updates to whatever this process can see, which for
        /// a document being archived is a change nobody asked for.
        /// </summary>
        public bool UpdateFields { get; set; }

        /// <summary>
        /// JPEG-compresses images in the output. Off keeps images lossless at the cost of size.
        /// </summary>
        public bool CompressImages { get; set; } = true;

        /// <summary>
        /// Renders to PDF/A-1b instead of plain PDF, for documents that must be archivable.
        /// </summary>
        public bool PdfACompliant { get; set; }
    }
}
