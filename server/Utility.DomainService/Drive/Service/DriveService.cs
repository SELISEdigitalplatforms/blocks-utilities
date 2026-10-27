using System.Net.Http.Json;
using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Utility.DomainService.Drive.Models;
using Utility.DomainService.Shared.Utilities;

namespace Utility.DomainService.Drive.Service;

public interface IDriveService
{
    Task<DriveResult<IReadOnlyList<SiteSummary>>> ListSitesAsync(ListSitesRequest request, CancellationToken cancellationToken);

    Task<DriveResult<ItemsPage>> ListItemsAsync(ListItemsRequest request, CancellationToken cancellationToken);

    Task<DriveResult<DriveItemSummary>> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken);

    Task<DriveResult<ImportFileResponse>> ImportAsync(ImportFileRequest request, CancellationToken cancellationToken);

    Task<DriveResult<UploadFilesResponse>> UploadAsync(UploadFilesRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Browse, folder creation, import into Blocks Storage, and upload out of it, for the calling
/// user's connected Microsoft account.
/// </summary>
/// <remarks>
/// OneDrive and SharePoint are the same Graph <c>driveItem</c> API once a drive id is known, so
/// every operation first resolves the drive (<c>/me/drive</c> or <c>/sites/{id}/drive</c>) and
/// then speaks <c>/drives/{id}/...</c>. A caller that already has the drive id from a previous
/// listing passes it and skips that round trip.
/// </remarks>
public sealed class DriveService : IDriveService
{
    private const string ItemSelect = "id,name,size,file,folder,createdDateTime,lastModifiedDateTime,webUrl,parentReference";
    private const int ChunkAlignment = 320 * 1024;

    private readonly IDriveConnectionService _connections;
    private readonly IGraphClient _graph;
    private readonly IDriveStorageBridge _storage;
    private readonly IOptions<DriveOptions> _options;
    private readonly DriveMetrics _metrics;
    private readonly IValidator<ListItemsRequest> _listValidator;
    private readonly IValidator<CreateFolderRequest> _folderValidator;
    private readonly IValidator<ImportFileRequest> _importValidator;
    private readonly IValidator<UploadFilesRequest> _uploadValidator;
    private readonly ILogger<DriveService> _logger;

    public DriveService(
        IDriveConnectionService connections,
        IGraphClient graph,
        IDriveStorageBridge storage,
        IOptions<DriveOptions> options,
        DriveMetrics metrics,
        IValidator<ListItemsRequest> listValidator,
        IValidator<CreateFolderRequest> folderValidator,
        IValidator<ImportFileRequest> importValidator,
        IValidator<UploadFilesRequest> uploadValidator,
        ILogger<DriveService> logger)
    {
        _connections = connections;
        _graph = graph;
        _storage = storage;
        _options = options;
        _metrics = metrics;
        _listValidator = listValidator;
        _folderValidator = folderValidator;
        _importValidator = importValidator;
        _uploadValidator = uploadValidator;
        _logger = logger;
    }

    public async Task<DriveResult<IReadOnlyList<SiteSummary>>> ListSitesAsync(ListSitesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = await _connections.GetAccessTokenAsync(cancellationToken);
        if (!token.IsSuccess)
        {
            return DriveResult<IReadOnlyList<SiteSummary>>.From(token);
        }

        var url = string.IsNullOrWhiteSpace(request.Search)
            ? $"{_graph.BaseUrl}/me/followedSites?$select=id,displayName,name,webUrl"
            : $"{_graph.BaseUrl}/sites?search={Uri.EscapeDataString(request.Search)}&$select=id,displayName,name,webUrl";

        var response = await _graph.SendAsync("sites.list", token.Value!, () => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        if (!response.IsSuccess)
        {
            return DriveResult<IReadOnlyList<SiteSummary>>.From(response);
        }

        using var graph = response.Value!;
        var sites = new List<SiteSummary>();
        if (graph.Root.TryGetProperty("value", out var value))
        {
            foreach (var site in value.EnumerateArray())
            {
                sites.Add(new SiteSummary
                {
                    SiteId = Str(site, "id") ?? string.Empty,
                    Name = Str(site, "displayName") ?? Str(site, "name") ?? string.Empty,
                    WebUrl = Str(site, "webUrl"),
                });
            }
        }

        return DriveResult<IReadOnlyList<SiteSummary>>.Ok(sites);
    }

    public async Task<DriveResult<ItemsPage>> ListItemsAsync(ListItemsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _listValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return DriveResult<ItemsPage>.Fail(DriveFailureKind.InvalidRequest, validation.ToString("; "));
        }

        var token = await _connections.GetAccessTokenAsync(cancellationToken);
        if (!token.IsSuccess)
        {
            return DriveResult<ItemsPage>.From(token);
        }

        string url;
        var sortApplied = true;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            var decoded = _graph.DecodeCursor(request.Cursor);
            if (decoded is null)
            {
                return DriveResult<ItemsPage>.Fail(DriveFailureKind.InvalidRequest, "cursor is not valid.");
            }

            url = decoded;
            sortApplied = string.IsNullOrEmpty(request.Search);
        }
        else
        {
            var drive = await ResolveDriveIdAsync(request.Source, request.SiteId, request.DriveId, token.Value!, cancellationToken);
            if (!drive.IsSuccess)
            {
                return DriveResult<ItemsPage>.From(drive);
            }

            var path = ItemPath(drive.Value!, request.ParentId);
            var query = new List<string> { $"$select={ItemSelect}", $"$top={request.PageSize ?? _options.Value.DefaultPageSize}" };

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                // Graph cannot order search results; saying so beats silently ignoring sortBy.
                url = $"{path}/search(q='{Uri.EscapeDataString(request.Search.Replace("'", "''", StringComparison.Ordinal))}')";
                sortApplied = false;
            }
            else
            {
                url = $"{path}/children";
                if (!string.IsNullOrEmpty(request.SortBy))
                {
                    var order = string.Equals(request.SortOrder, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
                    query.Add($"$orderby={Uri.EscapeDataString($"{request.SortBy} {order}")}");
                }
            }

            url += "?" + string.Join("&", query);
        }

        var listUrl = url;
        var response = await _graph.SendAsync("items.list", token.Value!, () => new HttpRequestMessage(HttpMethod.Get, listUrl), cancellationToken);
        if (!response.IsSuccess)
        {
            return DriveResult<ItemsPage>.From(response);
        }

        using var graph = response.Value!;
        var items = new List<DriveItemSummary>();
        if (graph.Root.TryGetProperty("value", out var value))
        {
            foreach (var item in value.EnumerateArray())
            {
                items.Add(ToSummary(item));
            }
        }

        return DriveResult<ItemsPage>.Ok(new ItemsPage
        {
            Items = items,
            NextCursor = _graph.EncodeCursor(Str(graph.Root, "@odata.nextLink")),
            SortApplied = sortApplied,
        });
    }

    public async Task<DriveResult<DriveItemSummary>> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _folderValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return DriveResult<DriveItemSummary>.Fail(DriveFailureKind.InvalidRequest, validation.ToString("; "));
        }

        var token = await _connections.GetAccessTokenAsync(cancellationToken);
        if (!token.IsSuccess)
        {
            return DriveResult<DriveItemSummary>.From(token);
        }

        var drive = await ResolveDriveIdAsync(request.Source, request.SiteId, request.DriveId, token.Value!, cancellationToken);
        if (!drive.IsSuccess)
        {
            return DriveResult<DriveItemSummary>.From(drive);
        }

        var url = $"{ItemPath(drive.Value!, request.ParentId)}/children";
        var body = new Dictionary<string, object>
        {
            ["name"] = request.Name!,
            ["folder"] = new Dictionary<string, object>(),
            // Fail rather than rename: a silently created "Contracts (1)" is worse than a 409.
            ["@microsoft.graph.conflictBehavior"] = "fail",
        };

        var response = await _graph.SendAsync(
            "folders.create",
            token.Value!,
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return DriveResult<DriveItemSummary>.From(response);
        }

        using var graph = response.Value!;
        return DriveResult<DriveItemSummary>.Ok(ToSummary(graph.Root));
    }

    public async Task<DriveResult<ImportFileResponse>> ImportAsync(ImportFileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _importValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return DriveResult<ImportFileResponse>.Fail(DriveFailureKind.InvalidRequest, validation.ToString("; "));
        }

        var token = await _connections.GetAccessTokenAsync(cancellationToken);
        if (!token.IsSuccess)
        {
            return DriveResult<ImportFileResponse>.From(token);
        }

        var itemUrl = $"{_graph.BaseUrl}/drives/{request.DriveId}/items/{request.ItemId}";
        var metadata = await _graph.SendAsync("items.get", token.Value!, () => new HttpRequestMessage(HttpMethod.Get, itemUrl), cancellationToken);
        if (!metadata.IsSuccess)
        {
            return DriveResult<ImportFileResponse>.From(metadata);
        }

        string name;
        long size;
        string? mimeType;
        string? downloadUrl;
        using (var graph = metadata.Value!)
        {
            if (graph.Root.TryGetProperty("folder", out _))
            {
                return DriveResult<ImportFileResponse>.Fail(DriveFailureKind.InvalidRequest, "The item is a folder.");
            }

            name = Str(graph.Root, "name") ?? request.ItemId!;
            size = graph.Root.TryGetProperty("size", out var s) && s.TryGetInt64(out var parsed) ? parsed : 0;
            mimeType = graph.Root.TryGetProperty("file", out var file) ? Str(file, "mimeType") : null;
            downloadUrl = Str(graph.Root, "@microsoft.graph.downloadUrl");
        }

        if (size > _options.Value.MaxFileSizeBytes)
        {
            return DriveResult<ImportFileResponse>.Fail(DriveFailureKind.FileTooLarge, $"The file exceeds {_options.Value.MaxFileSizeBytes} bytes.");
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            return DriveResult<ImportFileResponse>.Fail(DriveFailureKind.ProviderError, "Microsoft Graph returned no download URL.");
        }

        // The download URL is pre-authenticated and lives on a different host; no bearer token.
        var download = await _graph.SendAsync("items.download", string.Empty, () => new HttpRequestMessage(HttpMethod.Get, downloadUrl), cancellationToken, streamBody: true);
        if (!download.IsSuccess)
        {
            return DriveResult<ImportFileResponse>.From(download);
        }

        using var content = download.Value!;
        var fileId = Guid.NewGuid().ToString();
        await using var stream = await content.Message!.Content.ReadAsStreamAsync(cancellationToken);
        var length = content.Message.Content.Headers.ContentLength ?? (size > 0 ? size : null);

        var saved = await _storage.SaveAsync(fileId, name, mimeType, stream, length, cancellationToken);
        if (!saved)
        {
            return DriveResult<ImportFileResponse>.Fail(DriveFailureKind.StorageError, "Blocks Storage did not accept the file.");
        }

        _metrics.ImportedBytes(length ?? size);
        _logger.LogInformation("Drive import saved fileId={FileId}, size={Size}", fileId, length ?? size);

        return DriveResult<ImportFileResponse>.Ok(new ImportFileResponse { FileId = fileId, Name = name, Size = length ?? size, MimeType = mimeType });
    }

    public async Task<DriveResult<UploadFilesResponse>> UploadAsync(UploadFilesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await _uploadValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return DriveResult<UploadFilesResponse>.Fail(DriveFailureKind.InvalidRequest, validation.ToString("; "));
        }

        var token = await _connections.GetAccessTokenAsync(cancellationToken);
        if (!token.IsSuccess)
        {
            return DriveResult<UploadFilesResponse>.From(token);
        }

        var drive = await ResolveDriveIdAsync(request.Source, request.SiteId, request.DriveId, token.Value!, cancellationToken);
        if (!drive.IsSuccess)
        {
            return DriveResult<UploadFilesResponse>.From(drive);
        }

        var results = new List<UploadFileResult>();
        foreach (var fileId in request.FileIds!)
        {
            results.Add(await UploadOneAsync(drive.Value!, request.ParentId, fileId, token.Value!, cancellationToken));
        }

        return DriveResult<UploadFilesResponse>.Ok(new UploadFilesResponse { Results = results });
    }

    private async Task<UploadFileResult> UploadOneAsync(string driveId, string? parentId, string fileId, string accessToken, CancellationToken cancellationToken)
    {
        using var handle = await _storage.OpenAsync(fileId, cancellationToken);
        if (handle is null)
        {
            return Failed(fileId, DriveFailureKind.StorageError, "The file could not be read from Blocks Storage.");
        }

        var content = handle.Content;
        var length = handle.Length;
        FileStream? spill = null;
        try
        {
            if (length is null)
            {
                // ponytail: storage gave no Content-Length; spill to a temp file to learn it rather
                // than hold the whole thing in memory. Replace with a chunked-unknown-length session if it ever matters.
                spill = new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
                await content.CopyToAsync(spill, cancellationToken);
                spill.Position = 0;
                content = spill;
                length = spill.Length;
            }

            if (length > _options.Value.MaxFileSizeBytes)
            {
                return Failed(fileId, DriveFailureKind.FileTooLarge, $"The file exceeds {_options.Value.MaxFileSizeBytes} bytes.");
            }

            var target = $"{ItemPath(driveId, parentId)}:/{Uri.EscapeDataString(handle.Name)}:";
            var result = length <= _options.Value.SimpleUploadLimitBytes
                ? await SimpleUploadAsync(target, content, length.Value, accessToken, cancellationToken)
                : await SessionUploadAsync(target, handle.Name, content, length.Value, accessToken, cancellationToken);

            if (!result.IsSuccess)
            {
                return Failed(fileId, result.FailureKind, result.ErrorMessage ?? "Upload failed.");
            }

            _metrics.UploadedBytes(length.Value);
            return new UploadFileResult { FileId = fileId, Success = true, Item = result.Value };
        }
        finally
        {
            if (spill is not null)
            {
                await spill.DisposeAsync();
            }
        }
    }

    private async Task<DriveResult<DriveItemSummary>> SimpleUploadAsync(string target, Stream content, long length, string accessToken, CancellationToken cancellationToken)
    {
        // Bounded by SimpleUploadLimitBytes, so holding it is cheap, and it is what makes a retry
        // after a 429 resend the whole body rather than whatever the first attempt left unread.
        var body = new byte[length];
        await content.ReadExactlyAsync(body, cancellationToken);

        var url = $"{target}/content?@microsoft.graph.conflictBehavior=rename";
        var response = await _graph.SendAsync(
            "items.upload",
            accessToken,
            () => new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(body) },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return DriveResult<DriveItemSummary>.From(response);
        }

        using var graph = response.Value!;
        return DriveResult<DriveItemSummary>.Ok(ToSummary(graph.Root));
    }

    private async Task<DriveResult<DriveItemSummary>> SessionUploadAsync(string target, string name, Stream content, long length, string accessToken, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object>
        {
            ["item"] = new Dictionary<string, object>
            {
                ["@microsoft.graph.conflictBehavior"] = "rename",
                ["name"] = name,
            },
        };

        var session = await _graph.SendAsync(
            "items.uploadSession",
            accessToken,
            () => new HttpRequestMessage(HttpMethod.Post, $"{target}/createUploadSession") { Content = JsonContent.Create(body) },
            cancellationToken);

        if (!session.IsSuccess)
        {
            return DriveResult<DriveItemSummary>.From(session);
        }

        string? uploadUrl;
        using (var graph = session.Value!)
        {
            uploadUrl = Str(graph.Root, "uploadUrl");
        }

        if (string.IsNullOrEmpty(uploadUrl))
        {
            return DriveResult<DriveItemSummary>.Fail(DriveFailureKind.ProviderError, "Microsoft Graph returned no upload session URL.");
        }

        var chunkSize = Math.Max(ChunkAlignment, _options.Value.UploadChunkSizeBytes / ChunkAlignment * ChunkAlignment);
        var buffer = new byte[chunkSize];
        long offset = 0;

        while (offset < length)
        {
            var toRead = (int)Math.Min(chunkSize, length - offset);
            var read = await content.ReadAtLeastAsync(buffer.AsMemory(0, toRead), toRead, throwOnEndOfStream: false, cancellationToken);
            if (read == 0)
            {
                return DriveResult<DriveItemSummary>.Fail(DriveFailureKind.StorageError, "The storage stream ended early.");
            }

            var start = offset;
            var end = offset + read - 1;

            // The session URL is pre-authenticated; Graph rejects a bearer header on it.
            var chunk = await _graph.SendAsync(
                "items.uploadChunk",
                string.Empty,
                () =>
                {
                    var message = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(buffer, 0, read) };
                    message.Content.Headers.ContentLength = read;
                    message.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, end, length);
                    return message;
                },
                cancellationToken);

            if (!chunk.IsSuccess)
            {
                return DriveResult<DriveItemSummary>.From(chunk);
            }

            offset += read;
            using var graph = chunk.Value!;
            if (offset >= length)
            {
                return DriveResult<DriveItemSummary>.Ok(ToSummary(graph.Root));
            }
        }

        return DriveResult<DriveItemSummary>.Fail(DriveFailureKind.ProviderError, "The upload session ended without an item.");
    }

    private async Task<DriveResult<string>> ResolveDriveIdAsync(string? source, string? siteId, string? driveId, string accessToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(driveId))
        {
            return DriveResult<string>.Ok(driveId);
        }

        var url = string.Equals(source, DriveSource.SharePoint, StringComparison.OrdinalIgnoreCase)
            ? $"{_graph.BaseUrl}/sites/{Uri.EscapeDataString(siteId ?? string.Empty)}/drive?$select=id"
            : $"{_graph.BaseUrl}/me/drive?$select=id";

        var response = await _graph.SendAsync("drive.resolve", accessToken, () => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        if (!response.IsSuccess)
        {
            return DriveResult<string>.From(response);
        }

        using var graph = response.Value!;
        var id = Str(graph.Root, "id");
        return string.IsNullOrEmpty(id)
            ? DriveResult<string>.Fail(DriveFailureKind.ProviderError, "Microsoft Graph returned no drive id.")
            : DriveResult<string>.Ok(id);
    }

    private string ItemPath(string driveId, string? parentId) =>
        string.IsNullOrEmpty(parentId)
            ? $"{_graph.BaseUrl}/drives/{driveId}/root"
            : $"{_graph.BaseUrl}/drives/{driveId}/items/{Uri.EscapeDataString(parentId)}";

    private static UploadFileResult Failed(string fileId, DriveFailureKind kind, string message) =>
        new() { FileId = fileId, Success = false, Error = $"{kind}: {message}" };

    private static DriveItemSummary ToSummary(JsonElement item)
    {
        var parent = item.TryGetProperty("parentReference", out var p) ? p : default;
        return new DriveItemSummary
        {
            DriveId = (parent.ValueKind == JsonValueKind.Object ? Str(parent, "driveId") : null) ?? string.Empty,
            ItemId = Str(item, "id") ?? string.Empty,
            Name = Str(item, "name") ?? string.Empty,
            Kind = item.TryGetProperty("folder", out _) ? "folder" : "file",
            Size = item.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
            MimeType = item.TryGetProperty("file", out var file) ? Str(file, "mimeType") : null,
            CreatedAt = Date(item, "createdDateTime"),
            ModifiedAt = Date(item, "lastModifiedDateTime"),
            WebUrl = Str(item, "webUrl"),
            ParentId = parent.ValueKind == JsonValueKind.Object ? Str(parent, "id") : null,
        };
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTime? Date(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDateTime(out var date) ? date.ToUniversalTime() : null;
}
