using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Utility.DomainService.Drive.Service;

/// <summary>One Graph response, with the body parsed when there is one.</summary>
public sealed class GraphResponse : IDisposable
{
    public HttpStatusCode StatusCode { get; init; }

    public JsonDocument? Body { get; init; }

    public HttpResponseMessage? Message { get; init; }

    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;

    public JsonElement Root => Body?.RootElement ?? default;

    public void Dispose()
    {
        Body?.Dispose();
        Message?.Dispose();
    }
}

public interface IGraphClient
{
    /// <summary>
    /// Sends a Graph request, retrying throttled responses. <paramref name="request"/> is a factory
    /// because an <see cref="HttpRequestMessage"/> cannot be sent twice.
    /// </summary>
    Task<DriveResult<GraphResponse>> SendAsync(
        string operation,
        string accessToken,
        Func<HttpRequestMessage> request,
        CancellationToken cancellationToken,
        bool streamBody = false);

    string BaseUrl { get; }

    string? EncodeCursor(string? nextLink);

    /// <summary>Null when the cursor is not one of ours or points somewhere other than Graph.</summary>
    string? DecodeCursor(string cursor);
}

/// <summary>
/// The one place Graph is spoken to. Owns retry, status-to-failure mapping, and cursor encoding.
/// </summary>
/// <remarks>
/// Graph throttles with 429 and, under load, 503, both with <c>Retry-After</c>. The loop honors it
/// up to <see cref="DriveOptions.MaxRetries"/> times and then gives up with
/// <see cref="DriveFailureKind.Throttled"/> and the last hint, so the signature app can back off
/// instead of hammering. Paging cursors are the Graph <c>@odata.nextLink</c>, base64url-encoded;
/// a cursor is followed only if it still points at the configured Graph host, because following
/// an arbitrary URL with the user's bearer token would hand that token to whoever minted the cursor.
/// </remarks>
public sealed class GraphClient : IGraphClient
{
    public const string HttpClientName = "utility-drive-graph";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<DriveOptions> _options;
    private readonly DriveMetrics _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GraphClient> _logger;

    public GraphClient(
        IHttpClientFactory httpClientFactory,
        IOptions<DriveOptions> options,
        DriveMetrics metrics,
        TimeProvider timeProvider,
        ILogger<GraphClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _metrics = metrics;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string BaseUrl => _options.Value.GraphBaseUrl.TrimEnd('/');

    public async Task<DriveResult<GraphResponse>> SendAsync(
        string operation,
        string accessToken,
        Func<HttpRequestMessage> request,
        CancellationToken cancellationToken,
        bool streamBody = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var maxAttempts = _options.Value.MaxRetries + 1;
        int? lastRetryAfter = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var message = request();
            if (!string.IsNullOrEmpty(accessToken) && message.Headers.Authorization is null)
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            var started = Stopwatch.GetTimestamp();
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                    message,
                    streamBody ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                message.Dispose();
                _logger.LogError(ex, "Graph {Operation}: request failed", operation);
                return DriveResult<GraphResponse>.Fail(DriveFailureKind.ProviderError, "Microsoft Graph could not be reached.");
            }

            _metrics.GraphRequest(operation, (int)response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                _metrics.Throttled();
                lastRetryAfter = RetryAfterSeconds(response);
                response.Dispose();
                message.Dispose();

                _logger.LogWarning("Graph {Operation}: throttled, attempt {Attempt}/{Max}, retryAfter={RetryAfter}s",
                    operation, attempt, maxAttempts, lastRetryAfter);

                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(lastRetryAfter.Value), _timeProvider, cancellationToken);
                }

                continue;
            }

            message.Dispose();

            if (streamBody && response.IsSuccessStatusCode)
            {
                return DriveResult<GraphResponse>.Ok(new GraphResponse { StatusCode = response.StatusCode, Message = response });
            }

            JsonDocument? body = null;
            if (response.Content.Headers.ContentLength is not 0)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        body = JsonDocument.Parse(text);
                    }
                    catch (JsonException)
                    {
                        body = null;
                    }
                }
            }

            if (response.IsSuccessStatusCode)
            {
                return DriveResult<GraphResponse>.Ok(new GraphResponse { StatusCode = response.StatusCode, Body = body, Message = response });
            }

            var failure = MapFailure(response.StatusCode, body);
            _logger.LogWarning("Graph {Operation}: {Status} -> {Kind}", operation, (int)response.StatusCode, failure.FailureKind);
            body?.Dispose();
            response.Dispose();
            return failure;
        }

        return DriveResult<GraphResponse>.Fail(
            DriveFailureKind.Throttled,
            "Microsoft Graph is throttling requests. Retry later.",
            lastRetryAfter ?? _options.Value.DefaultRetryAfterSeconds);
    }

    public string? EncodeCursor(string? nextLink) =>
        string.IsNullOrEmpty(nextLink)
            ? null
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(nextLink)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string? DecodeCursor(string cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var url = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var graph)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, graph.Host, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return url;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private int RetryAfterSeconds(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
        }

        if (retryAfter?.Date is { } date)
        {
            return Math.Max(1, (int)Math.Ceiling((date - _timeProvider.GetUtcNow()).TotalSeconds));
        }

        return _options.Value.DefaultRetryAfterSeconds;
    }

    private static DriveResult<GraphResponse> MapFailure(HttpStatusCode status, JsonDocument? body)
    {
        string? code = null;
        string? message = null;
        if (body is not null
            && body.RootElement.ValueKind == JsonValueKind.Object
            && body.RootElement.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object)
        {
            code = error.TryGetProperty("code", out var c) ? c.GetString() : null;
            message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
        }

        return status switch
        {
            HttpStatusCode.NotFound => DriveResult<GraphResponse>.Fail(DriveFailureKind.ItemNotFound, message ?? "The drive item was not found."),
            HttpStatusCode.Conflict when code == "nameAlreadyExists" =>
                DriveResult<GraphResponse>.Fail(DriveFailureKind.NameConflict, message ?? "An item with that name already exists."),
            HttpStatusCode.Unauthorized => DriveResult<GraphResponse>.Fail(DriveFailureKind.NotConnected, "Microsoft rejected the access token."),
            HttpStatusCode.BadRequest => DriveResult<GraphResponse>.Fail(DriveFailureKind.InvalidRequest, message ?? "Microsoft Graph rejected the request."),
            HttpStatusCode.Forbidden => DriveResult<GraphResponse>.Fail(DriveFailureKind.ProviderError, message ?? "Access to the drive item was denied."),
            HttpStatusCode.RequestEntityTooLarge => DriveResult<GraphResponse>.Fail(DriveFailureKind.FileTooLarge, message ?? "The file is too large."),
            _ => DriveResult<GraphResponse>.Fail(DriveFailureKind.ProviderError, message ?? $"Microsoft Graph answered {(int)status}."),
        };
    }
}
