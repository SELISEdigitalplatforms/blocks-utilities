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

        private volatile IValidatorChannel? _channel;
        private volatile bool _ready;
        private long _loadedAtTicks;
        private long _nextRequestId;

        public PdfSignatureValidatorSupervisor(
            IOptions<PdfSignatureValidatorOptions> options,
            IValidatorChannelFactory factory,
            ILogger<PdfSignatureValidatorSupervisor> logger)
        {
            ArgumentNullException.ThrowIfNull(options);

            _options = options.Value;
            _factory = factory;
            _logger = logger;
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
                    if (!await PollStatusAsync(channel, TimeSpan.FromSeconds(_options.PerFileTimeoutSeconds), cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }
                }

                return responsive;
            }
            finally
            {
                _ready = false;
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
                var exchange = await ExchangeAsync(channel, "status", path: null, timeout, cancellationToken).ConfigureAwait(false);

                if (exchange.Kind != ExchangeKind.Reply)
                {
                    // Said here, where the two causes can still be told apart: a JVM that is slow and
                    // one that has died call for different fixes, and a message that blames the
                    // timeout for both sends an operator looking at the wrong one.
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
            _ready = ready;

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

                try
                {
                    exchange = await ExchangeAsync(channel, "validate", path, TimeSpan.FromSeconds(_options.PerFileTimeoutSeconds), cancellationToken).ConfigureAwait(false);
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
                        _logger.LogWarning(
                            "PdfSignatureValidatorSupervisor: A file took longer than {Seconds} s; restarting the DSS validator",
                            _options.PerFileTimeoutSeconds);
                        MarkDown(channel);
                        return PdfSignatureValidatorResult.TimedOut();

                    case ExchangeKind.Crashed:
                        _logger.LogError("PdfSignatureValidatorSupervisor: The DSS validator died or broke the protocol while validating a file; restarting it");
                        MarkDown(channel);
                        return PdfSignatureValidatorResult.Crashed();

                    default:
                        return Interpret(exchange.Reply);
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
                _ready = false;
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
            _ready = false;
            channel.Kill();
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
