using System.Collections.Concurrent;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Puts the subscription id into the text of every log line that knows it.
/// </summary>
/// <remarks>
/// The platform's log store keeps only the rendered message plus TenantId, TraceId and SpanId, and
/// the log console searches the message text. A <c>SubscriptionId</c> that lives in a log scope
/// reaches the sink as a property and is then thrown away, so a subscription's history could not be
/// found by its id. Appending the property to the message template here, once, covers every line
/// written inside a subscription's scope, including lines added later that do not name it.
/// </remarks>
public sealed class SubscriptionIdLogSink : ILogEventSink, IDisposable
{
    public const string PropertyName = "SubscriptionId";

    private static readonly MessageTemplateParser Parser = new();
    private static readonly ConcurrentDictionary<string, MessageTemplate> Extended = new();

    private readonly ILogger _inner;

    public SubscriptionIdLogSink(ILogger inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>
    /// Wraps the process-wide logger. Call once, after the platform has configured it and before
    /// the host is built, so every <c>ILogger&lt;T&gt;</c> the host creates writes through this.
    /// </summary>
    public static void Install() =>
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Sink(new SubscriptionIdLogSink(Log.Logger))
            .CreateLogger();

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        _inner.Write(ShouldAppend(logEvent) ? WithSubscriptionId(logEvent) : logEvent);
    }

    /// <summary>
    /// Whether this line should have <c>SubscriptionId=…</c> added to its message.
    /// </summary>
    /// <remarks>
    /// Only a real id: "none" marks tenant-wide sweep work and "missing" an absent value, and both
    /// lines already say what they are. Never twice: a template that names the id renders it itself.
    /// </remarks>
    internal static bool ShouldAppend(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue(PropertyName, out var value)
        && value is ScalarValue { Value: string id }
        && !string.IsNullOrWhiteSpace(id)
        && id is not ("none" or "missing")
        && !logEvent.MessageTemplate.Tokens
            .OfType<PropertyToken>()
            .Any(token => token.PropertyName == PropertyName);

    private static LogEvent WithSubscriptionId(LogEvent logEvent)
    {
        var template = Extended.GetOrAdd(
            logEvent.MessageTemplate.Text,
            text => Parser.Parse($"{text} {PropertyName}={{{PropertyName}}}"));

        return new LogEvent(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.Exception,
            template,
            logEvent.Properties.Select(property => new LogEventProperty(property.Key, property.Value)),
            logEvent.TraceId ?? default,
            logEvent.SpanId ?? default);
    }

    public void Dispose() => (_inner as IDisposable)?.Dispose();
}
