namespace Utility.DomainService.Shared.Utilities
{
    /// <summary>
    /// Makes caller-supplied values safe to write into a log.
    /// </summary>
    /// <remarks>
    /// Correlation IDs and file IDs arrive in a request body and are echoed straight into log
    /// messages. A value containing a newline splits one log entry into two, so a caller can forge
    /// entries that appear to come from the service itself — inventing an error that was never
    /// raised, or hiding a real one under a wall of plausible-looking lines. Structured logging does
    /// not prevent this: the rendered output a human or a log aggregator reads is still one line of
    /// text per newline in the value (CWE-117).
    ///
    /// Carriage returns and line feeds are removed rather than escaped, so the log keeps one entry
    /// per event. Remaining control characters are dropped too — a terminal reading the log will
    /// happily act on an escape sequence embedded in one. The value is truncated as well, because an
    /// unbounded field is a cheap way to flood log storage.
    /// </remarks>
    public static class LogSanitizer
    {
        /// <summary>
        /// Longest sanitized value written to a log. Comfortably longer than the GUIDs and
        /// correlation IDs this is used for, so a legitimate value is never cut.
        /// </summary>
        private const int MaxLength = 200;

        /// <summary>
        /// Returns <paramref name="value"/> with newlines and other control characters removed and
        /// its length capped, ready to be logged.
        /// </summary>
        public static string Scrub(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            // Explicit newline removal first: this is the injection that matters, and naming it
            // keeps the intent obvious to a reader (and to the static analysis that flags CWE-117).
            var scrubbed = value.Replace("\r", string.Empty, StringComparison.Ordinal)
                                .Replace("\n", string.Empty, StringComparison.Ordinal);

            if (scrubbed.Any(char.IsControl))
            {
                scrubbed = new string(scrubbed.Where(c => !char.IsControl(c)).ToArray());
            }

            return scrubbed.Length > MaxLength
                ? string.Concat(scrubbed.AsSpan(0, MaxLength), "...[truncated]")
                : scrubbed;
        }
    }
}
