using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Utility.DomainService.PdfSignatureValidation.Validator;

namespace XUnitTest.PdfSignatureValidation;

/// <remarks>
/// Guards the behaviour the spec promises around the validator process (AC-13, AC-14): a file that
/// runs past the timeout must cost one restart and a <c>validation_timeout</c>, not a hung worker; a
/// request must never be sent to a process that is still loading its trusted lists, or it would wait
/// in the pipe, time out, and kill the process it was waiting for; and one file at a time means a
/// second caller queues behind the first rather than reading its reply. Runs against a fake channel
/// because a real JVM takes most of a minute to start.
/// </remarks>
public sealed class PdfSignatureValidatorSupervisorTests
{
    [Fact]
    public async Task A_ready_validator_serves_a_file_and_returns_its_parsed_verdict()
    {
        await using var harness = new Harness(new FakeFactory());
        await harness.StartAsync();

        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");
        var result = await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);

        result.Outcome.Should().Be(PdfSignatureValidatorOutcome.Verdict);
        var verdict = result.Verdict!;
        verdict.SignatureCount.Should().Be(1);
        verdict.AllPassed.Should().BeTrue();
        verdict.Signatures.Should().ContainSingle().Which.RevocationOrigin.Should().Be(
            "DssDictionary", "the verdict's per-signature fields must survive the trip from the validator's JSON");
        harness.Supervisor.TrustedListsLoadedAt.Should().Be(
            new DateTime(2026, 9, 30, 2, 31, 0, 709, DateTimeKind.Utc), "the health signal for how fresh the trust is comes from the validator");
    }

    [Fact]
    public async Task Files_are_turned_away_as_not_ready_until_the_trusted_lists_have_loaded()
    {
        var loaded = false;
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "status" ? Status(request, ready: loaded) : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();

        await WaitUntilAsync(() => first.Ops.Contains("status"), "the first status check");
        var early = await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);

        early.Outcome.Should().Be(PdfSignatureValidatorOutcome.NotReady, "with no lists there is nothing to validate against");
        first.Ops.Should().NotContain(op => op.StartsWith("validate", StringComparison.Ordinal),
            "a request sent to a process still loading would wait in the pipe and trip the timeout that kills it");

        loaded = true;
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the next status poll to see the lists loaded");
        (await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None)).Outcome.Should().Be(PdfSignatureValidatorOutcome.Verdict);
    }

    [Fact]
    public async Task A_file_that_runs_past_the_timeout_kills_the_process_and_a_fresh_one_takes_the_next_file()
    {
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "validate" ? null : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        var result = await harness.Supervisor.ValidateAsync("slow.pdf", CancellationToken.None);

        result.Outcome.Should().Be(PdfSignatureValidatorOutcome.TimedOut);
        first.Killed.Should().BeTrue("a process stuck on one file cannot be reused, and its late reply would be read as the next file's");
        harness.Supervisor.IsReady.Should().BeFalse("until the replacement has loaded, files must wait rather than fail");

        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the replacement process to become ready");
        harness.Factory.Started.Should().HaveCount(2);
        var next = await harness.Supervisor.ValidateAsync("next.pdf", CancellationToken.None);
        next.Outcome.Should().Be(PdfSignatureValidatorOutcome.Verdict, "one slow file must not poison the ones behind it");
        harness.Factory.Started[1].Ops.Should().Contain("validate:next.pdf");
    }

    [Fact]
    public async Task A_process_that_dies_while_validating_is_reported_crashed_and_replaced()
    {
        using var first = new FakeChannel();
        first.Respond = request =>
        {
            if (Operation(request) != "validate")
            {
                return Serve(first, request);
            }

            first.Crash();
            return null;
        };
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        var result = await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);

        result.Outcome.Should().Be(PdfSignatureValidatorOutcome.Crashed);
        await WaitUntilAsync(() => harness.Supervisor.IsReady && harness.Factory.Started.Count == 2, "a replacement process");
    }

    [Fact]
    public async Task A_reply_carrying_someone_elses_id_is_treated_as_a_broken_conversation()
    {
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "validate"
            ? VerdictLine("not-the-id-we-sent")
            : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        var result = await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);

        result.Outcome.Should().Be(
            PdfSignatureValidatorOutcome.Crashed, "trusting a reply that is not ours could hand one file's verdict to another");
        first.Killed.Should().BeTrue();
    }

    [Fact]
    public async Task The_validator_saying_its_lists_are_gone_is_not_ready_and_not_a_rejection()
    {
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "validate"
            ? ErrorLine(request, "trust_lists_unavailable")
            : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        var result = await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);

        result.Outcome.Should().Be(
            PdfSignatureValidatorOutcome.NotReady, "nothing was decided about the file, so it must be retried, never failed");
        first.Killed.Should().BeFalse("the process is healthy; it only lacks its lists");
    }

    [Fact]
    public async Task A_rejection_about_one_file_reports_its_code_and_keeps_the_process()
    {
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "validate" && Path(request) == "bad.pdf"
            ? ErrorLine(request, "input_not_pdf")
            : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        var rejected = await harness.Supervisor.ValidateAsync("bad.pdf", CancellationToken.None);
        var next = await harness.Supervisor.ValidateAsync("good.pdf", CancellationToken.None);

        rejected.Outcome.Should().Be(PdfSignatureValidatorOutcome.Rejected);
        rejected.ErrorCode.Should().Be("input_not_pdf", "the caller reports the validator's own code for the file");
        next.Outcome.Should().Be(PdfSignatureValidatorOutcome.Verdict);
        harness.Factory.Started.Should().ContainSingle("a bad input is not a reason to restart a healthy process");
    }

    [Fact]
    public async Task Files_are_validated_one_at_a_time_and_a_second_caller_waits_its_turn()
    {
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "validate" ? null : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first), options => options.PerFileTimeoutSeconds = 10);
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        var one = harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);
        await WaitUntilAsync(() => first.Ops.Contains("validate:a.pdf"), "the first file to reach the process");
        var two = harness.Supervisor.ValidateAsync("b.pdf", CancellationToken.None);
        await Task.Delay(300);

        first.Ops.Should().NotContain("validate:b.pdf", "the protocol pairs each reply with the request before it");

        first.Reply(VerdictLine(first.TakePendingId()));
        (await one).Outcome.Should().Be(PdfSignatureValidatorOutcome.Verdict);
        await WaitUntilAsync(() => first.Ops.Contains("validate:b.pdf"), "the second file to reach the process");
        first.Reply(VerdictLine(first.TakePendingId()));
        (await two).Outcome.Should().Be(PdfSignatureValidatorOutcome.Verdict);
    }

    [Fact]
    public async Task A_process_that_never_answers_its_first_status_is_killed_and_replaced()
    {
        using var first = new FakeChannel { Respond = _ => null };
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();

        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the replacement process to become ready");

        first.Killed.Should().BeTrue("a JVM that never starts serving is stuck, and waiting longer will not help");
        harness.Factory.Started.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_process_that_cannot_be_started_is_retried()
    {
        await using var harness = new Harness(new FakeFactory(() => throw new InvalidOperationException("java not found")));
        await harness.StartAsync();

        await WaitUntilAsync(() => harness.Supervisor.IsReady, "a later attempt to start the process");

        harness.Factory.Started.Should().ContainSingle("the failed attempt never produced a process");
    }

    [Fact]
    public async Task A_process_that_fails_to_start_in_any_way_is_logged_and_never_takes_the_host_down()
    {
        // A kind of failure nobody listed: an unhandled exception out of a BackgroundService stops
        // the whole host, which would take payments and renewals down with a missing JVM.
        await using var harness = new Harness(new FakeFactory(() => throw new UnauthorizedAccessException("cache directory is read-only")));

        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "a later attempt to start the process");

        harness.Supervisor.ExecuteTask!.IsFaulted.Should().BeFalse("the hosted service must survive the validator failing");
        harness.Logger.Entries.Should().Contain(
            entry => entry.Level == LogLevel.Error && entry.Exception is UnauthorizedAccessException,
            "the failure is to be logged, with its exception, for whoever operates the worker");
    }

    [Fact]
    public async Task Starting_the_host_never_throws_even_when_the_first_start_attempt_throws_straight_away()
    {
        await using var harness = new Harness(new FakeFactory(() => throw new NotSupportedException("no such platform")));

        var start = () => harness.StartAsync();

        await start.Should().NotThrowAsync("a JVM that cannot start must never be able to fail the worker's own start-up");
    }

    [Fact]
    public async Task A_crashed_process_is_logged_as_an_error_and_the_service_keeps_running()
    {
        var first = new FakeChannel();
        first.Respond = request =>
        {
            if (Operation(request) != "validate")
            {
                return Serve(first, request);
            }

            first.Crash();
            return null;
        };
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);
        await WaitUntilAsync(() => harness.Supervisor.IsReady && harness.Factory.Started.Count == 2, "a replacement process");

        harness.Logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Error, "a dead JVM must leave an error in the log");
        harness.Supervisor.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task Validating_with_nothing_running_is_not_ready()
    {
        await using var harness = new Harness(new FakeFactory());

        var result = await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);

        result.Outcome.Should().Be(PdfSignatureValidatorOutcome.NotReady, "before the supervisor starts there is no process to ask");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string waitingFor)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {waitingFor}.");
            }

            await Task.Delay(20);
        }
    }

    private static string Operation(JsonElement request) => request.GetProperty("op").GetString()!;

    private static string Path(JsonElement request) => request.GetProperty("path").GetString()!;

    private static string Id(JsonElement request) => request.GetProperty("id").GetString()!;

    /// <summary>A healthy validator: ready on status, a passing one-signature verdict on validate.</summary>
    private static string Serve(FakeChannel channel, JsonElement request) =>
        Operation(request) == "status" ? Status(request, ready: true) : VerdictLine(Id(request));

    private static string Status(JsonElement request, bool ready) =>
        $$"""{"id":"{{Id(request)}}","ok":true,"ready":{{(ready ? "true" : "false")}},"trustedListsLoadedAt":"2026-09-30T02:31:00.709Z","trustedCertificateCount":4790}""";

    private static string ErrorLine(JsonElement request, string code) =>
        $$"""{"id":"{{Id(request)}}","ok":false,"errorCode":"{{code}}","errorMessage":"{{code}}"}""";

    private const string VerdictJson =
        """
        {"id":"__ID__","ok":true,"verdict":{"signatureCount":1,"lowestLevel":"PAdES-BASELINE-LT","allPassed":true,
        "validationTime":"2026-09-30T05:35:36.704Z","trustedListsLoadedAt":"2026-09-30T02:31:00.709Z",
        "signatures":[{"fieldName":"sig1","indication":"TOTAL_PASSED","subIndication":null,"signatureLevel":"PAdES-BASELINE-LT",
        "signerSubject":"CN=SELISE Group AG","claimedSigningTime":"2026-09-28T21:42:03Z","bestSignatureTime":"2026-09-28T21:39:03.700Z",
        "revocationOrigin":"DssDictionary","coversWholeDocument":true}]}}
        """;

    private static string VerdictLine(string id) =>
        VerdictJson.ReplaceLineEndings(string.Empty).Replace("__ID__", id, StringComparison.Ordinal);

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(FakeFactory factory, Action<PdfSignatureValidatorOptions>? configure = null)
        {
            Factory = factory;

            // Whole seconds are the smallest unit the options take, so the restart-heavy tests wait
            // real seconds; they are kept to the minimum that still proves each behaviour.
            var options = new PdfSignatureValidatorOptions
            {
                PerFileTimeoutSeconds = 1,
                StartupTimeoutSeconds = 1,
                StatusPollSeconds = 1,
                RestartDelaySeconds = 1
            };
            configure?.Invoke(options);

            Supervisor = new PdfSignatureValidatorSupervisor(Options.Create(options), factory, Logger);
        }

        public CapturingLogger Logger { get; } = new();

        public PdfSignatureValidatorSupervisor Supervisor { get; }

        public FakeFactory Factory { get; }

        public Task StartAsync() => Supervisor.StartAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAsync(CancellationToken.None);
            Supervisor.Dispose();
            Factory.Dispose();
        }
    }

    private sealed class CapturingLogger : ILogger<PdfSignatureValidatorSupervisor>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, exception));
            }
        }
    }

    private sealed class FakeFactory : IValidatorChannelFactory, IDisposable
    {
        private readonly Queue<Func<FakeChannel>> _plan;

        /// <summary>The first process started for each entry, then healthy ones once the plan runs out.</summary>
        public FakeFactory(params Func<FakeChannel>[] plan) => _plan = new Queue<Func<FakeChannel>>(plan);

        public List<FakeChannel> Started { get; } = [];

        public IValidatorChannel Start(PdfSignatureValidatorOptions options)
        {
            FakeChannel channel;

            lock (Started)
            {
                channel = _plan.Count > 0 ? _plan.Dequeue()() : Healthy();
                Started.Add(channel);
            }

            return channel;
        }

        public void Dispose()
        {
            lock (Started)
            {
                Started.ForEach(channel => channel.Dispose());
            }
        }

        private static FakeChannel Healthy()
        {
            var channel = new FakeChannel();
            channel.Respond = request => Serve(channel, request);
            return channel;
        }
    }

    private sealed class FakeChannel : IValidatorChannel
    {
        private readonly Channel<string> _replies = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Queue<string> _pendingIds = new();

        /// <summary>Answers a request with a reply line, or null to stay silent like a hung process.</summary>
        public Func<JsonElement, string?> Respond { get; set; } = _ => null;

        public List<string> Ops { get; } = [];

        public bool Killed { get; private set; }

        public Task Exited => _exited.Task;

        public Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(line);
            var request = document.RootElement.Clone();

            lock (Ops)
            {
                Ops.Add(Operation(request) == "validate" ? $"validate:{Path(request)}" : Operation(request));

                if (Operation(request) == "validate")
                {
                    _pendingIds.Enqueue(Id(request));
                }
            }

            var reply = Respond(request);

            if (reply is not null)
            {
                _replies.Writer.TryWrite(reply);
            }

            return Task.CompletedTask;
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
            await _replies.Reader.WaitToReadAsync(CancellationToken.None) && _replies.Reader.TryRead(out var line)
                ? line
                : null;

        public void Reply(string line) => _replies.Writer.TryWrite(line);

        public string TakePendingId()
        {
            lock (Ops)
            {
                return _pendingIds.Dequeue();
            }
        }

        /// <summary>The process dying by itself: output closes and the process is gone.</summary>
        public void Crash()
        {
            _replies.Writer.TryComplete();
            _exited.TrySetResult();
        }

        public void Kill()
        {
            Killed = true;
            Crash();
        }

        public void Dispose() => Kill();
    }
}
