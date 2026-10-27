using System.Diagnostics.Metrics;

namespace Utility.DomainService.Drive;

/// <summary>
/// The drive integration as numbers: connections coming and going, Graph latency, throttling,
/// and bytes moved. Exported by the Api through the meter named <see cref="MeterName"/>.
/// </summary>
public sealed class DriveMetrics : IDisposable
{
    public const string MeterName = "Blocks.Utility.Drive";

    private readonly Counter<long> _connectionsCreated;
    private readonly Counter<long> _connectionsRevoked;
    private readonly Counter<long> _throttled;
    private readonly Counter<long> _importBytes;
    private readonly Counter<long> _uploadBytes;
    private readonly Histogram<double> _graphDuration;

    public DriveMetrics()
    {
        Meter = new Meter(MeterName);

        _connectionsCreated = Meter.CreateCounter<long>("drive.connections.created", unit: "{connection}",
            description: "Successful OAuth callbacks that stored a connection.");
        _connectionsRevoked = Meter.CreateCounter<long>("drive.connections.revoked", unit: "{connection}",
            description: "Connections deleted because Entra rejected the refresh token, or by the user.");
        _throttled = Meter.CreateCounter<long>("drive.graph.throttled", unit: "{response}",
            description: "Graph responses that were 429 or 503, counted per response, not per request.");
        _importBytes = Meter.CreateCounter<long>("drive.imports.bytes", unit: "By",
            description: "Bytes copied from a drive into Blocks Storage.");
        _uploadBytes = Meter.CreateCounter<long>("drive.uploads.bytes", unit: "By",
            description: "Bytes copied from Blocks Storage into a drive.");
        _graphDuration = Meter.CreateHistogram<double>("drive.graph.request.duration", unit: "ms",
            description: "Wall time of one Graph HTTP round trip, tagged with operation and status.");
    }

    public Meter Meter { get; }

    public void ConnectionCreated() => _connectionsCreated.Add(1);

    public void ConnectionRevoked(string reason) => _connectionsRevoked.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void Throttled() => _throttled.Add(1);

    public void ImportedBytes(long bytes) => _importBytes.Add(bytes);

    public void UploadedBytes(long bytes) => _uploadBytes.Add(bytes);

    public void GraphRequest(string operation, int status, double milliseconds) =>
        _graphDuration.Record(milliseconds,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", status));

    public void Dispose() => Meter.Dispose();
}
