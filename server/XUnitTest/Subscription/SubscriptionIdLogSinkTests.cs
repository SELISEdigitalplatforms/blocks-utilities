using FluentAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// The log store keeps only the rendered message, so a subscription id that is not in the message
/// text cannot be searched for. The sink puts it there.
/// </summary>
public sealed class SubscriptionIdLogSinkTests
{
    private sealed class Capture : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (ILogger Logger, Capture Captured) Pipeline()
    {
        var captured = new Capture();
        var inner = new LoggerConfiguration().WriteTo.Sink(captured).CreateLogger();
        var outer = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new SubscriptionIdLogSink(inner))
            .CreateLogger();

        return (outer, captured);
    }

    [Fact]
    public void A_line_inside_a_subscription_scope_gets_the_id_in_its_message()
    {
        var (logger, captured) = Pipeline();

        using (Serilog.Context.LogContext.PushProperty("SubscriptionId", "sub-42"))
        {
            logger.Information("Subscription work completed DurationMs={DurationMs}", 7);
        }

        captured.Events.Single().RenderMessage()
            .Should().Be("Subscription work completed DurationMs=7 SubscriptionId=\"sub-42\"");
    }

    [Fact]
    public void A_line_that_already_names_the_id_is_not_given_it_twice()
    {
        var (logger, captured) = Pipeline();

        logger.Information("Subscription renewed SubscriptionId={SubscriptionId}", "sub-42");

        captured.Events.Single().RenderMessage()
            .Should().Be("Subscription renewed SubscriptionId=\"sub-42\"");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("missing")]
    public void A_placeholder_id_is_not_appended(string placeholder)
    {
        var (logger, captured) = Pipeline();

        using (Serilog.Context.LogContext.PushProperty("SubscriptionId", placeholder))
        {
            logger.Information("Subscription work completed DurationMs={DurationMs}", 7);
        }

        captured.Events.Single().RenderMessage().Should().Be("Subscription work completed DurationMs=7");
    }

    [Fact]
    public void A_line_with_no_subscription_is_left_alone()
    {
        var (logger, captured) = Pipeline();

        logger.Information("Payment work queue depth Count={Count}", 3);

        captured.Events.Single().RenderMessage().Should().Be("Payment work queue depth Count=3");
    }
}
