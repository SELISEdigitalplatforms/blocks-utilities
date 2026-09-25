using System.Collections.Concurrent;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace Subscription.DomainService.Utilities;

/// <summary>
/// Puts the subscription and payment ids into the text of every log line that knows them.
/// </summary>
/// <remarks>
/// The platform's log store keeps only the rendered message plus TenantId, TraceId and SpanId, and
/// the log console searches the message text. An id that lives in a log scope reaches the sink as a
/// property and is then thrown away, so a subscription's or a payment's history could not be found
/// by its id. Appending the property to the message template here, once, covers every line written
/// inside such a scope, including lines added later that do not name it.
/// </remarks>
public sealed class SearchableIdLogSink : ILogEventSink, IDisposable
{
    public const string SubscriptionId = "SubscriptionId";
    public const string PaymentId = "PaymentId";

    /// <summary>The ids appended, in the order they appear at the end of a line.</summary>
    private static readonly string[] PropertyNames = [SubscriptionId, PaymentId];

    private static readonly MessageTemplateParser Parser = new();
    private static readonly ConcurrentDictionary<string, MessageTemplate> Extended = new();

    private readonly ILogger _inner;

    public SearchableIdLogSink(ILogger inner) =>
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>
    /// Wraps the process-wide logger. Call once, after the platform has configured it and before
    /// the host is built, so every <c>ILogger&lt;T&gt;</c> the host creates writes through this.
    /// </summary>
    public static void Install() =>
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Sink(new SearchableIdLogSink(Log.Logger))
            .CreateLogger();

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var missing = PropertyNames.Where(name => ShouldAppend(logEvent, name)).ToArray();

        _inner.Write(missing.Length == 0 ? logEvent : WithIds(logEvent, missing));
    }

    /// <summary>
    /// Only a real id: "none" marks tenant-wide sweep work and "missing" an absent value, and both
    /// lines already say what they are. Never twice: a template that names the id renders it itself.
    /// </summary>
    internal static bool ShouldAppend(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out var value)
        && value is ScalarValue { Value: string id }
        && !string.IsNullOrWhiteSpace(id)
        && id is not ("none" or "missing")
        && !logEvent.MessageTemplate.Tokens
            .OfType<PropertyToken>()
            .Any(token => token.PropertyName == propertyName);

    private static LogEvent WithIds(LogEvent logEvent, string[] propertyNames)
    {
        var suffix = string.Concat(propertyNames.Select(name => $" {name}={{{name}}}"));
        var template = Extended.GetOrAdd(
            logEvent.MessageTemplate.Text + suffix,
            text => Parser.Parse(text));

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
