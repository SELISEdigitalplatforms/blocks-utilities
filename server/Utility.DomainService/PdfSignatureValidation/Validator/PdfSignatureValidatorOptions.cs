namespace Utility.DomainService.PdfSignatureValidation.Validator
{
    /// <summary>
    /// How the Worker runs and supervises the EU DSS validator process (<c>tools/dss</c>).
    /// </summary>
    /// <remarks>
    /// The path defaults are where <c>Dockerfile.worker</c> puts things, so a deployed worker needs
    /// no configuration; a developer running the Worker outside the image overrides them.
    /// </remarks>
    public sealed class PdfSignatureValidatorOptions
    {
        public const string SectionName = "PdfSignatureValidation";

        public string JavaPath { get; set; } = "/usr/bin/java";

        /// <summary>
        /// The JVM expands the trailing <c>/*</c> itself, so this needs no shell. The separator is the
        /// platform's, which is <c>:</c> in the Linux image this runs in.
        /// </summary>
        public string ClassPath { get; set; } = "/opt/dss/lib/*:/opt/dss";

        /// <summary>
        /// Where the validator keeps the downloaded EU trusted lists. It has to outlive the JVM, which
        /// is restarted after every timeout or crash, so that a restart reloads from disk instead of
        /// downloading again; it does not have to outlive the container. See the spec's Runtime section.
        /// </summary>
        public string TrustedListCacheDirectory { get; set; } = "/tmp/dss-tl-cache";

        public string LotlSignersPath { get; set; } = "/opt/dss/eu-lotl-signers.pem";

        /// <summary>Optional PEM file of extra trust anchors, per environment. Empty means none.</summary>
        public string? ExtraAnchorsPath { get; set; }

        /// <summary>How often the validator refreshes the trusted lists from the network.</summary>
        public int TrustedListRefreshHours { get; set; } = 24;

        /// <summary>
        /// The JVM's heap cap. Sized so the worker's existing tools (Chromium, Ghostscript, PDFBox)
        /// keep their headroom; the spec's starting point is 512 MB.
        /// </summary>
        public int HeapMegabytes { get; set; } = 512;

        /// <summary>
        /// The longest one file may take before the JVM is killed and restarted. 30 s, not longer:
        /// the spec's 1-minute target for a single file has to hold even for a file that runs to the
        /// timeout, and raising this past about 45 s breaks it.
        /// </summary>
        public int PerFileTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// The timeout for the first file a process validates after it starts, before it has produced
        /// a verdict. The JVM has not yet run the validation code path, so that file alone takes
        /// 13 to 27 s (30 s on a loaded machine) against <see cref="PerFileTimeoutSeconds"/>; later
        /// files take 2 to 6 s. Without the allowance a slow first file times out, which restarts the
        /// process, and the next first file pays the same cost again. A synthetic warm-up document
        /// was tried and did not remove the cost (see the spec's Deferred Decisions), so this is the
        /// fix. Never shorter than <see cref="PerFileTimeoutSeconds"/>.
        /// </summary>
        public int FirstFileTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// The longest a freshly started JVM may take to begin answering. Much longer than a file
        /// timeout because it covers loading the ~4,800 certificates of the cached lists, which the
        /// spike measured at 40 to 50 s, plus headroom for a slower disk.
        /// </summary>
        public int StartupTimeoutSeconds { get; set; } = 180;

        /// <summary>How often the supervisor asks the JVM whether the trusted lists have loaded.</summary>
        public int StatusPollSeconds { get; set; } = 10;

        /// <summary>Pause before restarting a JVM that died, so a broken install cannot spin.</summary>
        public int RestartDelaySeconds { get; set; } = 2;

        /// <summary>
        /// How long a job may wait for the trusted lists to load, measured from the request, before it
        /// fails with <c>trust_lists_unavailable</c>. Covers a new pod's first download (about two
        /// minutes) with room to spare.
        /// </summary>
        public int TrustedListWaitMinutes { get; set; } = 10;

        /// <summary>The delay before a job that found the validator not ready is tried again.</summary>
        public int NotReadyRetrySeconds { get; set; } = 15;
    }
}
