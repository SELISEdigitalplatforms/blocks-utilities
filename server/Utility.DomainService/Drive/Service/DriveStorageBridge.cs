using Blocks.Genesis;
using Blocks.Genesis.Entities.Data;
using DomainService.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StorageDriver;
using Utility.DomainService.Shared.Utilities;
using Utility.DomainService.Storage;

namespace Utility.DomainService.Drive.Service;

/// <summary>An open Blocks Storage file: its name, its bytes, and how many if storage said.</summary>
public sealed class StorageFileHandle : IDisposable
{
    public string Name { get; init; } = string.Empty;

    public Stream Content { get; init; } = Stream.Null;

    public long? Length { get; init; }

    public HttpResponseMessage? Response { get; init; }

    public void Dispose()
    {
        Content.Dispose();
        Response?.Dispose();
    }
}

public interface IDriveStorageBridge
{
    Task<StorageFileHandle?> OpenAsync(string fileId, CancellationToken cancellationToken);

    Task<bool> SaveAsync(string fileId, string name, string? mimeType, Stream content, long? length, CancellationToken cancellationToken);
}

/// <summary>
/// Blocks Storage as seen from the drive module: open a file to push it out, save a stream that
/// came in.
/// </summary>
/// <remarks>
/// Streams end to end. <see cref="StorageHelperBase.GetFileStreamFromUrl"/> and
/// <c>PdfStorageHelper.SavePdfToStorage</c> both buffer the whole file in a
/// <see cref="MemoryStream"/>, which is fine for a generated PDF and fatal for a 250 MB signed
/// archive under 100 concurrent users, so this class does its own GET with headers-only
/// completion and hands the content stream straight to the pre-signed PUT.
/// </remarks>
public sealed class DriveStorageBridge : StorageHelperBase, IDriveStorageBridge
{
    private readonly IStorageDriverService _storage;
    private readonly IOptions<DriveOptions> _options;

    public DriveStorageBridge(
        ILogger<DriveStorageBridge> logger,
        IHttpClientFactory httpClientFactory,
        IStorageDriverService storage,
        IOptions<DriveOptions> options,
        StorageDirectoryResolver? directories = null)
        : base(logger, httpClientFactory, directories)
    {
        _storage = storage;
        _options = options;
    }

    public async Task<StorageFileHandle?> OpenAsync(string fileId, CancellationToken cancellationToken)
    {
        var record = await _storage.GetUrlForDownloadFileAsync(new GetFileRequest { FileId = fileId });
        if (record is null || string.IsNullOrEmpty(record.Url))
        {
            _logger.LogError("Drive storage open: no record or URL for fileId={FileId}, response: {Response}",
                LogSanitizer.Scrub(fileId), Describe(record));
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, record.Url);
        request.Headers.Add("X-Blocks-Key", BlocksContext.GetContext()?.TenantId);

        var response = await CreateHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Drive storage open: download failed fileId={FileId}, status={Status}", LogSanitizer.Scrub(fileId), response.StatusCode);
            response.Dispose();
            return null;
        }

        return new StorageFileHandle
        {
            Name = string.IsNullOrWhiteSpace(record.Name) ? fileId : record.Name,
            Content = await response.Content.ReadAsStreamAsync(cancellationToken),
            Length = response.Content.Headers.ContentLength ?? (record.SizeInBytes > 0 ? record.SizeInBytes : null),
            Response = response,
        };
    }

    public async Task<bool> SaveAsync(string fileId, string name, string? mimeType, Stream content, long? length, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var parentDirectory = await ResolveParentDirectoryAsync(_options.Value.ImportDirectory);
        if (parentDirectory is null)
        {
            _logger.LogError("Drive storage save: no storage directory for {Directory}", LogSanitizer.Scrub(_options.Value.ImportDirectory));
            return false;
        }

        var upload = await _storage.GetPerSignedUrlForUploadAsync(new GetPreSignedUrlForUploadRequest
        {
            ItemId = fileId,
            Name = name,
            ParentDirectoryId = parentDirectory,
            Tags = "[\"Drive\"]",
            AccessModifier = "Private",
            MetaData = string.Empty,
            SizeInBytes = length,
            ContentType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType,
        });

        if (upload is null || string.IsNullOrEmpty(upload.UploadUrl))
        {
            _logger.LogError("Drive storage save: no upload URL for fileId={FileId}, response: {Response}", LogSanitizer.Scrub(fileId), Describe(upload));
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl)
        {
            Content = new StreamContent(content)
        };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType);
        if (length is { } known)
        {
            request.Content.Headers.ContentLength = known;
        }

        using var response = await CreateHttpClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Drive storage save: upload failed fileId={FileId}, status={Status}, body: {Body}",
                LogSanitizer.Scrub(fileId), response.StatusCode, await DescribeFailureAsync(response));
            return false;
        }

        if (!upload.UploadCompletionRequired)
        {
            return true;
        }

        var completion = await _storage.CompleteUploadAsync(new CompleteUploadRequest
        {
            FileId = fileId,
            FileVersionId = upload.FileVersionId ?? string.Empty,
        });

        if (completion?.VerificationStatus != FileVerificationStatus.Verified)
        {
            _logger.LogError("Drive storage save: completion rejected fileId={FileId}, status={Status}, response: {Response}",
                LogSanitizer.Scrub(fileId), completion?.VerificationStatus, Describe(completion));
            return false;
        }

        return true;
    }
}
