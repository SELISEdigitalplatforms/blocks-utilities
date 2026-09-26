using FluentAssertions;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Sms.DomainService.Utilities;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Sms;

/// <summary>
/// The log store keeps the rendered message and the tenant, nothing else. An SMS line has to carry
/// its message id and correlation id in the text to be found again.
/// </summary>
public sealed class SmsLogScopeTests
{
    private sealed class Capture : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    // The production pipeline: Microsoft ILogger → Serilog with log context → SearchableIdLogSink.
    private static (Microsoft.Extensions.Logging.ILogger Logger, Capture Captured, IDisposable Factory) Pipeline()
    {
        var captured = new Capture();
        var inner = new LoggerConfiguration().WriteTo.Sink(captured).CreateLogger();
        var outer = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new SearchableIdLogSink(inner))
            .CreateLogger();
        var factory = new SerilogLoggerFactory(outer, dispose: true);

        return (factory.CreateLogger("Sms"), captured, factory);
    }

    [Fact]
    public void Every_line_in_the_scope_names_its_message_and_correlation()
    {
        var (logger, captured, factory) = Pipeline();
        using (factory)
        using (SmsLogScope.Begin(logger, "tenant-a", "order-42", "msg-1"))
        {
            logger.LogInformation("SmsService: accepted Recipients={Recipients}", 2);
        }

        var line = captured.Events.Single();
        line.RenderMessage().Should().Be("SmsService: accepted Recipients=2 SmsMessageId=\"msg-1\" CorrelationId=\"order-42\"");
        line.Properties["TenantId"].ToString().Should().Be("\"tenant-a\"");
    }

    [Fact]
    public void An_inner_scope_without_a_message_keeps_the_outer_message_id()
    {
        var (logger, captured, factory) = Pipeline();
        using (factory)
        using (SmsLogScope.Begin(logger, "tenant-a", "order-42", "msg-1"))
        using (SmsLogScope.Begin(logger, "tenant-a", "order-42"))
        {
            logger.LogWarning("inner");
        }

        captured.Events.Single().RenderMessage().Should().Contain("SmsMessageId=\"msg-1\"");
    }

    [Fact]
    public void A_missing_correlation_is_left_out_rather_than_printed()
    {
        var (logger, captured, factory) = Pipeline();
        using (factory)
        using (SmsLogScope.Begin(logger, "tenant-a", correlationId: null))
        {
            logger.LogWarning("SmsWebhookService: callback rejected");
        }

        captured.Events.Single().RenderMessage().Should().Be("SmsWebhookService: callback rejected");
    }

    [Fact]
    public void Scope_values_are_sanitized()
    {
        var (logger, captured, factory) = Pipeline();
        using (factory)
        using (SmsLogScope.Begin(logger, "tenant-a", "abc\r\nFAKE ENTRY", "msg-1"))
        {
            logger.LogInformation("line");
        }

        captured.Events.Single().RenderMessage().Should().NotContain("\n").And.Contain("CorrelationId=\"abcFAKEENTRY\"");
    }
}
