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
    private static readonly DateTime ClockStart = new(2026, 9, 30, 2, 40, 0, DateTimeKind.Utc);

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
    public async Task A_validator_unavailable_past_the_wait_logs_critical_once_and_says_when_it_recovers()
    {
        var loaded = false;
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "status" ? Status(request, ready: loaded) : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => first.Ops.Contains("status"), "the first status check");

        harness.Logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Critical, "a validator that is still loading is not yet a problem");

        harness.Time.UtcNow = ClockStart.AddMinutes(11);
        await WaitUntilAsync(() => harness.Logger.Entries.Any(entry => entry.Level == LogLevel.Critical), "the critical log");
        await Task.Delay(2500);

        harness.Logger.Entries.Count(entry => entry.Level == LogLevel.Critical).Should().Be(
            1, "it says so once, not on every poll, or it would bury the log it is trying to surface in");

        loaded = true;
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the lists to load");
        await WaitUntilAsync(
            () => harness.Logger.Entries.Any(entry => entry.Level == LogLevel.Information && entry.Message.Contains("ready again", StringComparison.Ordinal)),
            "the recovery to be logged");
    }

    [Fact]
    public async Task Starts_and_timeouts_are_counted_and_a_healthy_restart_is_not_a_crash()
    {
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "validate" ? null : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        await harness.Supervisor.ValidateAsync("slow.pdf", CancellationToken.None);
        await WaitUntilAsync(() => harness.Supervisor.IsReady && harness.Factory.Started.Count == 2, "the replacement process");

        harness.Capture.Total("pdf_signature_validation.validator.timeouts").Should().Be(1);
        harness.Capture.Total("pdf_signature_validation.validator.starts").Should().Be(2, "the original process and its replacement");
        harness.Capture.Total("pdf_signature_validation.validator.crashes").Should().Be(0, "a timeout is counted as a timeout, not also as a crash");
    }

    [Fact]
    public async Task A_process_that_dies_while_validating_is_counted_as_a_crash()
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

        await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);

        // Whoever claims the death first reports it: the request, or the restart loop a moment earlier.
        // Either way it is counted once, so wait for that rather than assuming which side it was.
        await WaitUntilAsync(() => harness.Capture.Total("pdf_signature_validation.validator.crashes") >= 1, "the crash to be counted");
        await Task.Delay(300);

        harness.Capture.Total("pdf_signature_validation.validator.crashes").Should().Be(1, "reported once, not once per party that noticed");
        harness.Capture.Total("pdf_signature_validation.validator.timeouts").Should().Be(0);
    }

    [Fact]
    public async Task The_ready_and_age_gauges_follow_the_validators_state()
    {
        var loaded = false;
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "status" ? Status(request, ready: loaded) : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => first.Ops.Contains("status"), "the first status check");

        harness.Capture.Collect();
        harness.Capture.Gauge("pdf_signature_validation.validator.ready").Should().Be(0, "an alert rule compares this to zero");
        harness.Capture.Gauge("pdf_signature_validation.trusted_lists.age").Should().BeNull(
            "no lists have loaded, and an age alert must not fire for a worker that is merely still starting");

        loaded = true;
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the lists to load");
        harness.Time.UtcNow = new DateTime(2026, 9, 30, 5, 31, 0, 709, DateTimeKind.Utc);

        harness.Capture.Collect();
        harness.Capture.Gauge("pdf_signature_validation.validator.ready").Should().Be(1);
        harness.Capture.Gauge("pdf_signature_validation.trusted_lists.age").Should().BeApproximately(
            10800, 1, "the lists loaded at 02:31:00.709 and it is now 05:31:00.709, three hours later");
    }

    [Fact]
    public async Task A_process_that_dies_on_its_own_while_idle_is_logged_counted_and_replaced()
    {
        // The case nothing else reports: no file is in flight, so no timeout or failed request marks
        // the death. A JVM killed for memory between files looks exactly like this.
        using var first = new FakeChannel();
        first.Respond = request => Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first));
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        first.Crash();

        await WaitUntilAsync(() => harness.Supervisor.IsReady && harness.Factory.Started.Count == 2, "a replacement process");
        harness.Logger.Entries.Should().Contain(
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("exited on its own", StringComparison.Ordinal),
            "an operator needs to see that the JVM died, not just that a new one started");
        harness.Capture.Total("pdf_signature_validation.validator.crashes").Should().Be(1);
    }

    [Fact]
    public async Task A_timeout_is_reported_once_even_when_a_status_poll_was_waiting_behind_the_file()
    {
        using var first = new FakeChannel();
        first.Respond = request => Operation(request) == "validate" ? null : Serve(first, request);
        await using var harness = new Harness(new FakeFactory(() => first), options => options.StatusPollSeconds = 1);
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        // The file holds the gate for the whole timeout, so the poll that falls due meanwhile queues.
        await harness.Supervisor.ValidateAsync("slow.pdf", CancellationToken.None);
        await WaitUntilAsync(() => harness.Supervisor.IsReady && harness.Factory.Started.Count == 2, "the replacement process");

        harness.Capture.Total("pdf_signature_validation.validator.crashes").Should().Be(
            0, "the poll found the process already killed for the timeout, which was reported where it happened");
        harness.Logger.Entries.Count(entry => entry.Level == LogLevel.Error).Should().Be(0);
    }

    [Fact]
    public async Task Only_the_first_file_after_a_start_gets_the_longer_timeout()
    {
        // Every validate answers after 1.5 s: past the 1 s per-file limit, inside the 3 s first-file one.
        using var first = new FakeChannel();
        first.Respond = request =>
        {
            if (Operation(request) != "validate")
            {
                return Serve(first, request);
            }

            var id = Id(request);
            _ = Task.Delay(1500).ContinueWith(_ => first.Reply(VerdictLine(id)), TaskScheduler.Default);
            return null;
        };
        await using var harness = new Harness(new FakeFactory(() => first), options =>
        {
            options.PerFileTimeoutSeconds = 1;
            options.FirstFileTimeoutSeconds = 3;
        });
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        var firstFile = await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);
        var secondFile = await harness.Supervisor.ValidateAsync("b.pdf", CancellationToken.None);

        firstFile.Outcome.Should().Be(
            PdfSignatureValidatorOutcome.Verdict,
            "the first file of a cold JVM is slow for reasons that say nothing about the file, and timing it out would restart the JVM for nothing");
        secondFile.Outcome.Should().Be(
            PdfSignatureValidatorOutcome.TimedOut, "once the JVM has produced a verdict the normal limit applies");
    }

    [Fact]
    public async Task The_longer_timeout_applies_again_to_the_first_file_of_every_replacement_process()
    {
        // Without this, a first file that really is slow on a loaded pod would time out, restart the
        // JVM, and have the next first file time out the same way, for ever.
        static FakeChannel HangingOnValidate()
        {
            var channel = new FakeChannel();
            channel.Respond = request => Operation(request) == "validate" ? null : Serve(channel, request);
            return channel;
        }

        await using var harness = new Harness(new FakeFactory(HangingOnValidate, HangingOnValidate), options =>
        {
            options.PerFileTimeoutSeconds = 1;
            options.FirstFileTimeoutSeconds = 2;
        });
        await harness.StartAsync();
        await WaitUntilAsync(() => harness.Supervisor.IsReady, "the validator to report ready");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var first = await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);
        var firstTook = clock.Elapsed;

        await WaitUntilAsync(() => harness.Supervisor.IsReady && harness.Factory.Started.Count == 2, "the replacement process");

        clock.Restart();
        var second = await harness.Supervisor.ValidateAsync("a.pdf", CancellationToken.None);

        first.Outcome.Should().Be(PdfSignatureValidatorOutcome.TimedOut, "even the longer limit ends a file that never answers");
        firstTook.Should().BeGreaterThan(TimeSpan.FromSeconds(1.8), "the first file waited for the first-file limit, not the per-file one");
        second.Outcome.Should().Be(PdfSignatureValidatorOutcome.TimedOut);
        clock.Elapsed.Should().BeGreaterThan(
            TimeSpan.FromSeconds(1.8), "the replacement process is cold too, so its first file also gets the longer limit");
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

    // Like the real validator: until something has loaded there is no load time to report.
    private static string Status(JsonElement request, bool ready) =>
        $$"""{"id":"{{Id(request)}}","ok":true,"ready":{{(ready ? "true" : "false")}},"trustedListsLoadedAt":{{(ready ? "\"2026-09-30T02:31:00.709Z\"" : "null")}},"trustedCertificateCount":{{(ready ? 4790 : 0)}}}""";

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

            Supervisor = new PdfSignatureValidatorSupervisor(Options.Create(options), factory, Logger, Metrics, Time);
            Capture = new MetricCapture(Metrics.Meter);
        }

        public CapturingLogger Logger { get; } = new();

        public PdfSignatureValidationMetrics Metrics { get; } = new();

        public FakeTimeProvider Time { get; } = new(ClockStart);

        public MetricCapture Capture { get; }

        public PdfSignatureValidatorSupervisor Supervisor { get; }

        public FakeFactory Factory { get; }

        public Task StartAsync() => Supervisor.StartAsync(CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAsync(CancellationToken.None);
            Supervisor.Dispose();
            Factory.Dispose();
            Capture.Dispose();
            Metrics.Dispose();
        }
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
    }

    private sealed class CapturingLogger : ILogger<PdfSignatureValidatorSupervisor>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, exception, formatter(state, exception)));
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
