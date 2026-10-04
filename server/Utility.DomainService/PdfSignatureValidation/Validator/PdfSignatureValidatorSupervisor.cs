using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Utility.DomainService.PdfSignatureValidation.Entities;

namespace Utility.DomainService.PdfSignatureValidation.Validator
{
    /// <summary>
    /// Keeps one EU DSS validator process running for the life of the worker, hands it one file at a
    /// time, and restarts it when it times out, crashes or exits.
    /// </summary>
    /// <remarks>
    /// One long-lived process, not one per file, because the expensive part is loading the trusted
    /// lists (about 50 s from the local cache, about 2 minutes cold) and a file costs a few seconds
    /// once they are in memory.
    /// <para>
    /// The life of each process is: start it; wait for its first reply, which only comes once it has
    /// loaded its cached lists and begun serving (<see cref="PdfSignatureValidatorOptions.StartupTimeoutSeconds"/>);
    /// then ask it every few seconds whether the lists are loaded, since an empty cache means it is
    /// serving while still downloading. <see cref="IsReady"/> is true only once it says so, and
    /// <see cref="ValidateAsync"/> answers <see cref="PdfSignatureValidatorOutcome.NotReady"/> without
    /// touching the process before then. That is what stops a request sent during a restart from
    /// waiting in the pipe, running into the per-file timeout, and killing the process it was waiting
    /// for - a restart loop with no end.
    /// </para>
    /// <para>
    /// Every way a process can end - exit, crash, a timeout this class enforces - completes the
    /// channel's <see cref="IValidatorChannel.Exited"/>, which is the one signal the restart loop
    /// waits on. Nothing else needs to tell it to restart.
    /// </para>
    /// </remarks>
    public sealed class PdfSignatureValidatorSupervisor : BackgroundService, IPdfSignatureValidator
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly PdfSignatureValidatorOptions _options;
        private readonly IValidatorChannelFactory _factory;
        private readonly ILogger<PdfSignatureValidatorSupervisor> _logger;

        // One conversation at a time: the protocol pairs each reply with the request before it, so
        // two writers would not know whose answer they read.
        private readonly SemaphoreSlim _gate = new(1, 1);

        private readonly PdfSignatureValidationMetrics _metrics;
        private readonly TimeProvider _time;

        private volatile IValidatorChannel? _channel;
        private volatile bool _ready;
        private long _loadedAtTicks;
        private long _nextRequestId;

        // When the validator last stopped being ready, or the supervisor was created if it never has
        // been; zero while it is ready. What the Critical log measures "unavailable for too long" from.
        private long _notReadySinceTicks;
        private volatile bool _unhealthyLogged;

        // Set by whoever first reports the current process's death. A process that dies mid-file is
        // noticed twice, by the restart loop seeing it exit and by the request that was waiting on
        // it; this makes sure it is logged and counted once.
        private int _deathReported;

        // Whether the current process has produced a verdict yet; false again for every new process.
        private volatile bool _firstVerdictServed;

        public PdfSignatureValidatorSupervisor(
            IOptions<PdfSignatureValidatorOptions> options,
            IValidatorChannelFactory factory,
            ILogger<PdfSignatureValidatorSupervisor> logger,
            PdfSignatureValidationMetrics metrics,
            TimeProvider time)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(time);

            _options = options.Value;
            _factory = factory;
            _logger = logger;
            _metrics = metrics;
            _time = time;

            _notReadySinceTicks = time.GetUtcNow().UtcDateTime.Ticks;
            metrics.ObserveValidator(() => _ready, () => TrustedListsLoadedAt, time);
        }

        public bool IsReady => _ready;

        public DateTime? TrustedListsLoadedAt =>
            Interlocked.Read(ref _loadedAtTicks) is var ticks and > 0
                ? new DateTime(ticks, DateTimeKind.Utc)
                : null;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // The validator is one feature of a worker that also runs payments, renewals and usage
            // rating, so nothing about its JVM may decide whether the worker boots or stays up.
            // Two things make that true here. An exception escaping this method stops the whole host
            // by default, so the loop below catches everything and only logs. And on runtimes where
            // BackgroundService.StartAsync runs this method inline up to its first await (.NET 10
            // no longer does), anything thrown in that stretch would fail host start-up, so yield
            // before doing anything.
            await Task.Yield();

            var consecutiveFailures = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await RunProcessAsync(stoppingToken).ConfigureAwait(false))
                    {
                        consecutiveFailures = 0;
                    }
                    else
                    {
                        consecutiveFailures++;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Deliberately every exception, not a list of the ones expected. Java missing, a
                    // bad path, a cache directory that cannot be written, a pipe that breaks: each is
                    // the validator failing, which is logged and retried, never a reason to stop the
                    // host. Attempts back off so a broken install does not spin.
                    consecutiveFailures++;
                    _logger.LogError(ex, "PdfSignatureValidatorSupervisor: The DSS validator process failed; it will be started again");
                }

                CheckUnhealthy();

                try
                {
                    await Task.Delay(RestartDelay(consecutiveFailures), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Runs one validator process from start to end. Returns whether it ever became responsive,
        /// which is what tells a healthy restart from a process that is failing to come up at all.
        /// </summary>
        private async Task<bool> RunProcessAsync(CancellationToken cancellationToken)
        {
            using var channel = _factory.Start(_options);
            _channel = channel;
            Interlocked.Exchange(ref _deathReported, 0);
            _firstVerdictServed = false;
            _metrics.ValidatorStarted();

            _logger.LogInformation("PdfSignatureValidatorSupervisor: DSS validator process started; waiting for it to load the trusted lists");

            var responsive = false;

            try
            {
                if (!await PollStatusAsync(channel, TimeSpan.FromSeconds(_options.StartupTimeoutSeconds), cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                responsive = true;

                while (await SleepUnlessExitedAsync(channel, cancellationToken).ConfigureAwait(false))
                {
                    CheckUnhealthy();

                    if (!await PollStatusAsync(channel, TimeSpan.FromSeconds(_options.PerFileTimeoutSeconds), cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }
                }

                // Out of the loop with the process gone. If nobody has reported it, it died on its
                // own between files - killed for memory, say - which nothing else would have logged.
                // One this class killed, or that a request saw die, was reported where that happened.
                if (!cancellationToken.IsCancellationRequested && ClaimDeath())
                {
                    _logger.LogError("PdfSignatureValidatorSupervisor: The DSS validator process exited on its own; restarting it");
                    _metrics.ValidatorCrashed();
                }

                return responsive;
            }
            finally
            {
                SetReady(false);
                _channel = null;
                channel.Kill();
            }
        }

        /// <summary>Waits one poll interval. False when the process exited during it.</summary>
        private async Task<bool> SleepUnlessExitedAsync(IValidatorChannel channel, CancellationToken cancellationToken)
        {
            using var sleep = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(TimeSpan.FromSeconds(_options.StatusPollSeconds), sleep.Token);

            var finished = await Task.WhenAny(delay, channel.Exited).ConfigureAwait(false);
            await sleep.CancelAsync().ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            return finished == delay;
        }

        /// <summary>
        /// Asks the process whether it is serving and has its lists. False means the process is gone
        /// or unresponsive and has been killed.
        /// </summary>
        private async Task<bool> PollStatusAsync(IValidatorChannel channel, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // A poll can be queued behind a file that then times out. By the time it gets the
                // gate the process has been killed and that timeout already logged and counted, so
                // asking the dead process would only report the same death a second time.
                if (channel.Exited.IsCompleted)
                {
                    return false;
                }

                var exchange = await ExchangeAsync(channel, "status", path: null, timeout, cancellationToken).ConfigureAwait(false);

                if (exchange.Kind != ExchangeKind.Reply)
                {
                    // Said here, where the two causes can still be told apart: a JVM that is slow and
                    // one that has died call for different fixes, and a message that blames the
                    // timeout for both sends an operator looking at the wrong one.
                    if (ClaimDeath())
                    {
                        if (exchange.Kind == ExchangeKind.TimedOut)
                        {
                            _logger.LogError(
                                "PdfSignatureValidatorSupervisor: The DSS validator did not answer within {Seconds} s; restarting it",
                                (int)timeout.TotalSeconds);
                        }
                        else
                        {
                            _logger.LogError("PdfSignatureValidatorSupervisor: The DSS validator process exited or broke the protocol; restarting it");
                        }

                        _metrics.ValidatorCrashed();
                    }

                    MarkDown(channel);
                    return false;
                }

                ApplyStatus(exchange.Reply);
                return true;
            }
            catch (OperationCanceledException)
            {
                MarkDown(channel);
                throw;
            }
            finally
            {
                _gate.Release();
            }
        }

        private void ApplyStatus(JsonElement reply)
        {
            var ready = reply.TryGetProperty("ready", out var readyElement) && readyElement.ValueKind == JsonValueKind.True;

            if (reply.TryGetProperty("trustedListsLoadedAt", out var loadedAt)
                && loadedAt.ValueKind == JsonValueKind.String
                && loadedAt.TryGetDateTime(out var when))
            {
                Interlocked.Exchange(ref _loadedAtTicks, when.ToUniversalTime().Ticks);
            }

            var wasReady = _ready;
            SetReady(ready);

            if (ready && !wasReady)
            {
                _logger.LogInformation(
                    "PdfSignatureValidatorSupervisor: DSS validator ready; trusted lists loaded at {LoadedAt:o}",
                    TrustedListsLoadedAt);
            }
            else if (!ready && wasReady)
            {
                _logger.LogWarning("PdfSignatureValidatorSupervisor: DSS validator reports its trusted lists are no longer loaded");
            }
        }

        public async Task<PdfSignatureValidatorResult> ValidateAsync(string path, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            // Checked before the gate, not only after it: a process that is still loading holds the
            // gate for its whole startup, and a caller must be told to come back, not made to wait.
            if (!_ready)
            {
                return PdfSignatureValidatorResult.NotReady();
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var channel = _channel;

                if (channel is null || !_ready)
                {
                    return PdfSignatureValidatorResult.NotReady();
                }

                Exchange exchange;

                // A process that has not produced a verdict yet has not run the validation code path,
                // so its first file pays for the JVM's cold start: about 4.5 s on a quiet machine, but
                // 17 to 30 s under CPU contention against a 30 s timeout. Timing that file out would
                // restart the process, and the next first file would pay the same again, so it gets
                // longer.
                var timeoutSeconds = _firstVerdictServed
                    ? _options.PerFileTimeoutSeconds
                    : Math.Max(_options.FirstFileTimeoutSeconds, _options.PerFileTimeoutSeconds);

                try
                {
                    exchange = await ExchangeAsync(channel, "validate", path, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The reply to a request nobody is waiting for any more would be read as the
                    // answer to the next one, so the process is not reused.
                    MarkDown(channel);
                    throw;
                }

                switch (exchange.Kind)
                {
                    case ExchangeKind.TimedOut:
                        // The timeout is this file's and is always reported; claiming the death only
                        // stops the restart loop reporting the kill that follows as a second event.
                        ClaimDeath();
                        _logger.LogWarning(
                            "PdfSignatureValidatorSupervisor: A file took longer than {Seconds} s; restarting the DSS validator",
                            timeoutSeconds);
                        _metrics.ValidatorTimedOut();
                        MarkDown(channel);
                        return PdfSignatureValidatorResult.TimedOut();

                    case ExchangeKind.Crashed:
                        if (ClaimDeath())
                        {
                            _logger.LogError("PdfSignatureValidatorSupervisor: The DSS validator died or broke the protocol while validating a file; restarting it");
                            _metrics.ValidatorCrashed();
                        }

                        MarkDown(channel);
                        return PdfSignatureValidatorResult.Crashed();

                    default:
                        var result = Interpret(exchange.Reply);

                        // Only a verdict counts: a rejection such as "not a PDF" comes back without
                        // running the validation code path, so the JVM is still cold after it.
                        if (result.Outcome == PdfSignatureValidatorOutcome.Verdict)
                        {
                            _firstVerdictServed = true;
                        }

                        return result;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private PdfSignatureValidatorResult Interpret(JsonElement reply)
        {
            if (reply.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var verdict = reply.TryGetProperty("verdict", out var element)
                    ? element.Deserialize<PdfSignatureValidationVerdict>(Json)
                    : null;

                return verdict is null
                    ? PdfSignatureValidatorResult.Rejected("validator_error", "The validator answered without a verdict.")
                    : PdfSignatureValidatorResult.Validated(verdict);
            }

            var code = reply.TryGetProperty("errorCode", out var codeElement) ? codeElement.GetString() : null;
            var message = reply.TryGetProperty("errorMessage", out var messageElement) ? messageElement.GetString() : null;

            if (code == "trust_lists_unavailable")
            {
                // The validator knows better than our last poll did. Not ready is a state, and the
                // next poll will say when it ends.
                SetReady(false);
                return PdfSignatureValidatorResult.NotReady();
            }

            return PdfSignatureValidatorResult.Rejected(code ?? "validator_error", message ?? "The validator reported an error.");
        }

        /// <summary>
        /// One request and its reply, bounded by <paramref name="timeout"/>. Only ever called holding
        /// the gate. Never throws for anything the process does; a caller cancelling is the exception.
        /// </summary>
        private async Task<Exchange> ExchangeAsync(
            IValidatorChannel channel,
            string op,
            string? path,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextRequestId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var request = JsonSerializer.Serialize(path is null ? new { op, id } : (object)new { op, id, path }, Json);

            var conversation = ConverseAsync(channel, request, id);

            // The timeout is a race against a timer, not a cancellation token handed to the read. A
            // read on a child process's redirected output does not reliably honour cancellation, and
            // a hung JVM is exactly the case this exists for; what ends the read is the caller
            // killing the process, which closes the pipe.
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var deadline = Task.Delay(timeout, timer.Token);

            var finished = await Task.WhenAny(conversation, deadline).ConfigureAwait(false);
            await timer.CancelAsync().ConfigureAwait(false);

            if (finished == conversation)
            {
                return await conversation.ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Exchange.TimedOut;
        }

        /// <summary>The write and the read of one request. Reports a broken conversation, never throws it.</summary>
        private static async Task<Exchange> ConverseAsync(IValidatorChannel channel, string request, string id)
        {
            try
            {
                await channel.WriteLineAsync(request, CancellationToken.None).ConfigureAwait(false);
                var line = await channel.ReadLineAsync(CancellationToken.None).ConfigureAwait(false);

                if (line is null)
                {
                    return Exchange.Crashed;
                }

                using var document = JsonDocument.Parse(line);

                // One conversation at a time means the next line is the reply; an id that is not ours
                // means it is not, and trusting it would hand one file's verdict to another.
                if (!document.RootElement.TryGetProperty("id", out var replyId) || replyId.GetString() != id)
                {
                    return Exchange.Crashed;
                }

                return new Exchange(ExchangeKind.Reply, document.RootElement.Clone());
            }
            catch (Exception)
            {
                // A process that died mid-conversation surfaces as any of a broken pipe, a disposed
                // stream or a half-written line; all of them mean the same thing to the caller.
                return Exchange.Crashed;
            }
        }

        private void MarkDown(IValidatorChannel channel)
        {
            SetReady(false);
            channel.Kill();
        }

        /// <summary>True for the first caller per process, which is then the one to report its death.</summary>
        private bool ClaimDeath() => Interlocked.Exchange(ref _deathReported, 1) == 0;

        /// <summary>
        /// The one place readiness changes, so the clock that "unavailable for too long" is measured
        /// against starts when it stops being ready and is cleared when it is ready again.
        /// </summary>
        private void SetReady(bool ready)
        {
            var wasReady = _ready;
            _ready = ready;

            if (ready)
            {
                Interlocked.Exchange(ref _notReadySinceTicks, 0);
            }
            else if (wasReady || Interlocked.Read(ref _notReadySinceTicks) == 0)
            {
                Interlocked.Exchange(ref _notReadySinceTicks, _time.GetUtcNow().UtcDateTime.Ticks);
            }
        }

        /// <summary>
        /// Says so, once, when the validator has been unavailable for as long as a job is allowed to
        /// wait for it - the moment files start failing with <c>trust_lists_unavailable</c>.
        /// </summary>
        /// <remarks>
        /// Critical rather than Error, for the reason the renderer's health gate gives: every other
        /// part of this worker keeps running by design, so nothing else will say why validations have
        /// gone quiet. A restart after a timeout or crash is expected to take a minute or two and is
        /// logged as it happens; this is for a validator that is not coming back.
        /// </remarks>
        private void CheckUnhealthy()
        {
            if (_ready)
            {
                if (_unhealthyLogged)
                {
                    _unhealthyLogged = false;
                    _logger.LogInformation("PdfSignatureValidatorSupervisor: The DSS validator is ready again; signature validation resumes");
                }

                return;
            }

            var sinceTicks = Interlocked.Read(ref _notReadySinceTicks);

            if (_unhealthyLogged || sinceTicks == 0)
            {
                return;
            }

            var unavailableFor = _time.GetUtcNow().UtcDateTime - new DateTime(sinceTicks, DateTimeKind.Utc);

            if (unavailableFor < TimeSpan.FromMinutes(_options.TrustedListWaitMinutes))
            {
                return;
            }

            _unhealthyLogged = true;
            _logger.LogCritical(
                "PdfSignatureValidatorSupervisor: The DSS validator has been unavailable for {Minutes:F0} minutes. " +
                "Signature validation is failing with trust_lists_unavailable until it recovers; payments, " +
                "renewals and everything else in this worker continue normally.",
                unavailableFor.TotalMinutes);
        }

        private TimeSpan RestartDelay(int consecutiveFailures)
        {
            var seconds = Math.Max(1, _options.RestartDelaySeconds);
            var backoff = Math.Min(seconds * Math.Pow(2, Math.Min(consecutiveFailures, 5)), 60);

            return TimeSpan.FromSeconds(consecutiveFailures == 0 ? seconds : backoff);
        }

        public override void Dispose()
        {
            _channel?.Dispose();
            _gate.Dispose();
            base.Dispose();
        }

        private enum ExchangeKind
        {
            Reply,
            TimedOut,
            Crashed
        }

        private readonly record struct Exchange(ExchangeKind Kind, JsonElement Reply)
        {
            public static Exchange TimedOut => new(ExchangeKind.TimedOut, default);

            public static Exchange Crashed => new(ExchangeKind.Crashed, default);
        }
    }
}
