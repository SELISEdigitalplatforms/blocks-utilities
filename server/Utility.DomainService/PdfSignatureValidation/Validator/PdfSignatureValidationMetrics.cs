using System.Diagnostics.Metrics;

namespace Utility.DomainService.PdfSignatureValidation.Validator
{
    /// <summary>
    /// What the signature validator looks like from outside, as numbers rather than log lines.
    /// </summary>
    /// <remarks>
    /// Logs say what happened to each file, which is enough to investigate once somebody notices a
    /// problem. These exist to notice one: a validator that keeps timing out, a JVM that keeps
    /// dying, trusted lists that stopped refreshing. Exported by the Worker through OTLP, with the
    /// alert rules versioned under <c>monitoring/</c> next to the code whose behavior they describe.
    /// <para>
    /// The counters are written by the consumer (what became of each job) and by the supervisor
    /// (what became of the process). The two gauges are read from the supervisor's state, through
    /// callbacks it registers, because the supervisor already owns that state and a copy kept here
    /// could disagree with it.
    /// </para>
    /// </remarks>
    public sealed class PdfSignatureValidationMetrics : IDisposable
    {
        /// <summary>The name an exporter subscribes to.</summary>
        public const string MeterName = "Blocks.Utility.PdfSignatureValidation";

        private readonly Counter<long> _completed;
        private readonly Counter<long> _failed;
        private readonly Counter<long> _requeued;
        private readonly Counter<long> _starts;
        private readonly Counter<long> _timeouts;
        private readonly Counter<long> _crashes;

        public PdfSignatureValidationMetrics()
        {
            Meter = new Meter(MeterName);

            _completed = Meter.CreateCounter<long>(
                "pdf_signature_validation.jobs.completed",
                unit: "{job}",
                description: "Files whose signatures were validated and a verdict recorded, whatever the verdict says.");

            _failed = Meter.CreateCounter<long>(
                "pdf_signature_validation.jobs.failed",
                unit: "{job}",
                description: "Files that ended without a verdict. The error_code tag says why.");

            _requeued = Meter.CreateCounter<long>(
                "pdf_signature_validation.jobs.requeued",
                unit: "{job}",
                description: "Times a file was put back on the queue because the validator was not ready.");

            _starts = Meter.CreateCounter<long>(
                "pdf_signature_validation.validator.starts",
                unit: "{process}",
                description: "Validator processes started. Anything above one per worker start is a restart.");

            _timeouts = Meter.CreateCounter<long>(
                "pdf_signature_validation.validator.timeouts",
                unit: "{file}",
                description: "Files that ran past the per-file timeout and cost a validator restart.");

            _crashes = Meter.CreateCounter<long>(
                "pdf_signature_validation.validator.crashes",
                unit: "{process}",
                description: "Validator processes that died or broke the protocol.");
        }

        /// <summary>The meter these instruments belong to. Exposed so a test can listen to this instance alone.</summary>
        public Meter Meter { get; }

        public void JobCompleted(bool allPassed) =>
            _completed.Add(1, new KeyValuePair<string, object?>("all_passed", allPassed));

        public void JobFailed(string errorCode) =>
            _failed.Add(1, new KeyValuePair<string, object?>("error_code", errorCode));

        public void JobRequeued() => _requeued.Add(1);

        public void ValidatorStarted() => _starts.Add(1);

        public void ValidatorTimedOut() => _timeouts.Add(1);

        public void ValidatorCrashed() => _crashes.Add(1);

        /// <summary>
        /// Publishes the two gauges, reading the supervisor's state when an exporter collects.
        /// </summary>
        /// <remarks>
        /// Ready is 1 or 0 rather than a boolean because that is what an alert rule can compare. Age
        /// has no measurement at all until the lists have loaded once, rather than a misleading huge
        /// number, so an alert on age cannot fire for a worker that is merely still starting.
        /// </remarks>
        public void ObserveValidator(Func<bool> isReady, Func<DateTime?> trustedListsLoadedAt, TimeProvider time)
        {
            ArgumentNullException.ThrowIfNull(isReady);
            ArgumentNullException.ThrowIfNull(trustedListsLoadedAt);
            ArgumentNullException.ThrowIfNull(time);

            Meter.CreateObservableGauge(
                "pdf_signature_validation.validator.ready",
                () => isReady() ? 1 : 0,
                description: "1 while a validator process is up and the trusted lists are loaded, otherwise 0.");

            Meter.CreateObservableGauge(
                "pdf_signature_validation.trusted_lists.age",
                () => trustedListsLoadedAt() is { } loadedAt
                    ? [new Measurement<double>(Math.Max(0, (time.GetUtcNow().UtcDateTime - loadedAt).TotalSeconds))]
                    : Array.Empty<Measurement<double>>(),
                unit: "s",
                description: "How long ago the trusted lists in use were loaded. Grows when the daily refresh keeps failing.");
        }

        public void Dispose() => Meter.Dispose();
    }
}
