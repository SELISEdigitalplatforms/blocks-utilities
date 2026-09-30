namespace Utility.DomainService.PdfSignatureValidation.Utilities
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public static class PdfSignatureValidationConstants
    {
        /// <summary>
        /// Where accepted validations are queued. The Worker's subscription to it, and the
        /// <c>GetMessageConfiguration</c> entry <c>Worker/Program.cs</c> spreads in, arrive with the
        /// consumer; until then nothing reads this queue.
        /// </summary>
        public const string ValidatePdfSignaturesQueue = "blocks_pdf_signature_validation_listener";
    }
}
