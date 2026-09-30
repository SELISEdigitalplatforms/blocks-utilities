namespace Utility.DomainService.PdfSignatureValidation.Validator
{
    /// <summary>
    /// The line-oriented conversation with one validator process: one JSON object per line in, one
    /// per line out (<c>tools/dss/README.md</c>).
    /// </summary>
    /// <remarks>
    /// A seam so the supervisor's timeout and restart behaviour can be tested without a JVM, which
    /// takes most of a minute to start. The real implementation is <see cref="ValidatorProcessChannel"/>.
    /// </remarks>
    public interface IValidatorChannel : IDisposable
    {
        /// <summary>Completes when the process is gone, however it got there: exit, crash or kill.</summary>
        Task Exited { get; }

        Task WriteLineAsync(string line, CancellationToken cancellationToken);

        /// <summary>The next line the process printed, or null once it has closed its output.</summary>
        Task<string?> ReadLineAsync(CancellationToken cancellationToken);

        /// <summary>Ends the process now, with its whole process tree. Safe to call more than once.</summary>
        void Kill();
    }

    public interface IValidatorChannelFactory
    {
        /// <summary>Starts a new validator process. Throws if the process cannot be started at all.</summary>
        IValidatorChannel Start(PdfSignatureValidatorOptions options);
    }
}
