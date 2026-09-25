using FluentAssertions;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Subscription.DomainService.Enums;
using Subscription.DomainService.Utilities;

namespace XUnitTest.Subscription;

/// <summary>
/// Pins the logging behaviour the subscription id scopes are built around.
/// </summary>
/// <remarks>
/// Through Serilog's Microsoft.Extensions.Logging bridge, a scope value overrides a message
/// template value of the same name. So a scope that puts the wrong id under <c>SubscriptionId</c>
/// relabels even lines that name the right one, which is how invoice lines came to show a payment
/// id, and why the dispatcher only sets it for work that is about a subscription.
/// </remarks>
public sealed class LogScopePrecedenceTests
{
    private sealed class Capture : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public void A_scope_value_overrides_the_template_value_of_the_same_name()
    {
        var captured = new Capture();
        var serilog = new LoggerConfiguration().WriteTo.Sink(captured).CreateLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog));
        var logger = factory.CreateLogger("precedence");

        using (logger.BeginScope(new Dictionary<string, object?> { ["SubscriptionId"] = "from-scope" }))
        {
            logger.LogInformation("Issued SubscriptionId={SubscriptionId}", "from-template");
        }

        captured.Events.Single().RenderMessage().Should().Be("Issued SubscriptionId=\"from-scope\"");
    }

    [Fact]
    public void An_inner_scope_overrides_an_outer_one()
    {
        var captured = new Capture();
        var serilog = new LoggerConfiguration().WriteTo.Sink(captured).CreateLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddSerilog(serilog));
        var logger = factory.CreateLogger("precedence");

        // The shape the issuer and delivery rely on: they open their own subscription scope inside
        // the dispatcher's, and it has to be theirs that the line carries.
        using (logger.BeginScope(new Dictionary<string, object?> { ["SubscriptionId"] = "outer" }))
        using (SubscriptionWorkLogValue.SubscriptionScope(logger, "inner"))
        {
            logger.LogInformation("Financial document issued");
        }

        captured.Events.Single().Properties["SubscriptionId"].ToString().Should().Be("\"inner\"");
    }

    [Theory]
    [InlineData(SubscriptionWorkType.FinancialDocumentIssue, false)]
    [InlineData(SubscriptionWorkType.FinancialDocumentDelivery, false)]
    [InlineData(SubscriptionWorkType.Renewal, true)]
    [InlineData(SubscriptionWorkType.ActivationRecovery, true)]
    public void Only_subscription_keyed_work_is_labelled_with_a_subscription_id(
        SubscriptionWorkType workType,
        bool expected) =>
        SubscriptionWorkLogValue.AggregateIsSubscription(workType).Should().Be(expected);
}
