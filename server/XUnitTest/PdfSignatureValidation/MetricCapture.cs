using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace XUnitTest.PdfSignatureValidation;

/// <summary>
/// Collects what one <see cref="Meter"/> instance publishes, so a test can assert on the numbers an
/// exporter would see. Listens to that instance only: the meter's name is shared by every instance,
/// and tests run in parallel, so matching on the name alone would count other tests' measurements.
/// </summary>
internal sealed class MetricCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, double> _counterSums = new();
    private readonly ConcurrentDictionary<string, double> _gaugeValues = new();
    private readonly ConcurrentQueue<(string Name, double Value, IReadOnlyDictionary<string, object?> Tags)> _counterMeasurements = new();

    public MetricCapture(Meter meter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    /// <summary>Asks every gauge for its current value, as an exporter does on each collection.</summary>
    public void Collect()
    {
        _gaugeValues.Clear();
        _listener.RecordObservableInstruments();
    }

    /// <summary>The total added to a counter so far; zero if it was never touched.</summary>
    public double Total(string counter) => _counterSums.TryGetValue(counter, out var sum) ? sum : 0;

    /// <summary>The total added to a counter with the given tag value.</summary>
    public double Total(string counter, string tag, object? value) =>
        _counterMeasurements
            .Where(m => m.Name == counter && m.Tags.TryGetValue(tag, out var actual) && Equals(actual, value))
            .Sum(m => m.Value);

    /// <summary>A gauge's value at the last <see cref="Collect"/>, or null if it published nothing.</summary>
    public double? Gauge(string name) => _gaugeValues.TryGetValue(name, out var value) ? value : null;

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (instrument.IsObservable)
        {
            _gaugeValues[instrument.Name] = value;
            return;
        }

        _counterSums.AddOrUpdate(instrument.Name, value, (_, sum) => sum + value);
        _counterMeasurements.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
    }
}
