using Utility.DomainService.PdfSignatureValidation.Entities;

namespace Utility.DomainService.PdfSignatureValidation.Validator
{
    /// <summary>
    /// Validates the signatures of one PDF on disk with the EU DSS validator process.
    /// </summary>
    public interface IPdfSignatureValidator
    {
        /// <summary>
        /// True while a validator process is up and the trusted lists are loaded. False while a
        /// process is starting, loading its cached lists, downloading them for the first time, or
        /// being restarted. A caller that finds it false should wait, not fail: it is a state, not a
        /// verdict.
        /// </summary>
        bool IsReady { get; }

        /// <summary>When the trusted lists in use were loaded; null until they have been.</summary>
        DateTime? TrustedListsLoadedAt { get; }

        /// <summary>
        /// Validates one file. One file at a time per process, so concurrent callers queue here.
        /// Never throws for anything the validator does; throws only when <paramref name="cancellationToken"/>
        /// is cancelled.
        /// </summary>
        Task<PdfSignatureValidatorResult> ValidateAsync(string path, CancellationToken cancellationToken);
    }

    public enum PdfSignatureValidatorOutcome
    {
        /// <summary>The file was validated; <see cref="PdfSignatureValidatorResult.Verdict"/> holds the answer.</summary>
        Verdict,

        /// <summary>
        /// The validator could not take the file yet: its trusted lists are not loaded. Nothing was
        /// decided about the file, so this is never a reason to fail a job.
        /// </summary>
        NotReady,

        /// <summary>The file ran past the per-file timeout and the validator process was restarted.</summary>
        TimedOut,

        /// <summary>The validator process died or broke the protocol while handling the file.</summary>
        Crashed,

        /// <summary>The validator answered, but with an error about this file. See <see cref="PdfSignatureValidatorResult.ErrorCode"/>.</summary>
        Rejected
    }

    public sealed class PdfSignatureValidatorResult
    {
        private PdfSignatureValidatorResult()
        {
        }

        public PdfSignatureValidatorOutcome Outcome { get; private init; }

        public PdfSignatureValidationVerdict? Verdict { get; private init; }

        public string? ErrorCode { get; private init; }

        public string? ErrorMessage { get; private init; }

        public static PdfSignatureValidatorResult Validated(PdfSignatureValidationVerdict verdict) =>
            new() { Outcome = PdfSignatureValidatorOutcome.Verdict, Verdict = verdict };

        public static PdfSignatureValidatorResult NotReady() =>
            new() { Outcome = PdfSignatureValidatorOutcome.NotReady };

        public static PdfSignatureValidatorResult TimedOut() =>
            new() { Outcome = PdfSignatureValidatorOutcome.TimedOut };

        public static PdfSignatureValidatorResult Crashed() =>
            new() { Outcome = PdfSignatureValidatorOutcome.Crashed };

        public static PdfSignatureValidatorResult Rejected(string errorCode, string errorMessage) =>
            new() { Outcome = PdfSignatureValidatorOutcome.Rejected, ErrorCode = errorCode, ErrorMessage = errorMessage };
    }
}
