using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Utility.DomainService.Drive;
using Utility.DomainService.Drive.Models;
using Utility.DomainService.Drive.Service;
using Utility.DomainService.Drive.Validators;

namespace XUnitTest.Drive;

/// <summary>
/// Guards the Graph requests the browse, folder, import and upload operations send, and how
/// their answers come back (AC-10 to AC-15, AC-19 to AC-28).
/// </summary>
/// <remarks>
/// Every Graph call is scripted and the exact URL, body and headers are asserted, because a
/// wrong <c>$orderby</c>, a missing conflict behavior, or a chunk whose <c>Content-Range</c> is off
/// by one are bugs that only a real tenant would otherwise reveal.
/// </remarks>
public sealed class DriveServiceTests : IDisposable
{
    private const string Graph = "https://graph.microsoft.com/v1.0";

    private readonly ScriptedHttpHandler _http = new();
    private readonly Mock<IDriveConnectionService> _connections = new();
    private readonly Mock<IDriveStorageBridge> _storage = new();
    private readonly DriveMetrics _metrics = new();
    private readonly DriveService _service;

    public DriveServiceTests()
    {
        _connections.Setup(c => c.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveResult<string>.Ok("token-1"));

        var options = DriveTestSupport.Options(o =>
        {
            o.SimpleUploadLimitBytes = 4 * 1024 * 1024;
            o.UploadChunkSizeBytes = 320 * 1024;
            o.MaxFileSizeBytes = 10 * 1024 * 1024;
        });

        _service = new DriveService(
            _connections.Object,
            new GraphClient(DriveTestSupport.Factory(_http), options, _metrics, DriveTestSupport.Clock(), NullLogger<GraphClient>.Instance),
            _storage.Object,
            options,
            _metrics,
            new ListItemsRequestValidator(options),
            new CreateFolderRequestValidator(),
            new ImportFileRequestValidator(),
            new UploadFilesRequestValidator(options),
            NullLogger<DriveService>.Instance);
    }

    public void Dispose() => _metrics.Dispose();

    [Fact]
    public async Task AC10_listing_onedrive_root_resolves_the_drive_and_maps_files_and_folders()
    {
        _http.On(HttpMethod.Get, "/me/drive?", HttpStatusCode.OK, "{\"id\":\"drive-1\"}");
        _http.On(HttpMethod.Get, "/drives/drive-1/root/children", HttpStatusCode.OK,
            $"{{\"value\":[{DriveTestSupport.Item("f1", "Contracts", folder: true)},{DriveTestSupport.Item("i1", "nda.pdf", size: 1234)}]}}");

        var result = await _service.ListItemsAsync(new ListItemsRequest { Source = "onedrive" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var items = result.Value!.Items;
        items.Should().HaveCount(2);
        items[0].Kind.Should().Be("folder");
        items[1].Kind.Should().Be("file");
        items[1].Size.Should().Be(1234);
        items[1].MimeType.Should().Be("application/pdf");
        items[1].DriveId.Should().Be("drive-1");
        items[1].ParentId.Should().Be("root-id");
        result.Value.NextCursor.Should().BeNull();
        result.Value.SortApplied.Should().BeTrue();
    }

    [Fact]
    public async Task AC11_a_next_link_becomes_a_cursor_and_the_cursor_fetches_the_next_page()
    {
        _http.On(HttpMethod.Get, "$skiptoken=page2", HttpStatusCode.OK, $"{{\"value\":[{DriveTestSupport.Item("b", "b.pdf")}]}}");
        _http.On(HttpMethod.Get, "/drives/drive-1/root/children?", HttpStatusCode.OK,
            $"{{\"value\":[{DriveTestSupport.Item("a", "a.pdf")}],\"@odata.nextLink\":\"{Graph}/drives/drive-1/root/children?$skiptoken=page2\"}}");

        var first = await _service.ListItemsAsync(new ListItemsRequest { Source = "onedrive", DriveId = "drive-1", PageSize = 1 }, CancellationToken.None);
        var second = await _service.ListItemsAsync(new ListItemsRequest { Cursor = first.Value!.NextCursor }, CancellationToken.None);

        first.Value.NextCursor.Should().NotBeNull();
        first.Value.Items.Single().ItemId.Should().Be("a");
        second.Value!.Items.Single().ItemId.Should().Be("b", because: "the cursor continues where the first page stopped");
        second.Value.NextCursor.Should().BeNull();
        _http.Requests.Should().HaveCount(2, because: "a supplied driveId skips the drive lookup");
    }

    [Fact]
    public async Task AC12_sorting_is_sent_as_orderby()
    {
        _http.On(HttpMethod.Get, "/drives/drive-1/root/children", HttpStatusCode.OK, "{\"value\":[]}");

        await _service.ListItemsAsync(new ListItemsRequest { Source = "onedrive", DriveId = "drive-1", SortBy = "name", SortOrder = "desc", PageSize = 25 }, CancellationToken.None);

        var url = _http.Requests.Single().Url;
        url.Should().Contain("$orderby=name%20desc");
        url.Should().Contain("$top=25");
    }

    [Fact]
    public async Task AC13_search_uses_the_search_route_and_reports_that_sort_was_not_applied()
    {
        _http.On(HttpMethod.Get, "/drives/drive-1/items/folder-9/search(q='contract')", HttpStatusCode.OK, "{\"value\":[]}");

        var result = await _service.ListItemsAsync(new ListItemsRequest { Source = "onedrive", DriveId = "drive-1", ParentId = "folder-9", Search = "contract", SortBy = "name" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SortApplied.Should().BeFalse(because: "Graph cannot order search results");
        _http.Requests.Single().Url.Should().NotContain("$orderby");
    }

    [Fact]
    public async Task AC14_a_sharepoint_listing_resolves_the_site_drive_first()
    {
        _http.On(HttpMethod.Get, "/sites/site-7/drive?", HttpStatusCode.OK, "{\"id\":\"drive-sp\"}");
        _http.On(HttpMethod.Get, "/drives/drive-sp/root/children", HttpStatusCode.OK, "{\"value\":[]}");

        var result = await _service.ListItemsAsync(new ListItemsRequest { Source = "sharepoint", SiteId = "site-7" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _http.Requests[0].Url.Should().StartWith($"{Graph}/sites/site-7/drive");
        _http.Requests[1].Url.Should().StartWith($"{Graph}/drives/drive-sp/root/children");
    }

    [Fact]
    public async Task AC15_sites_come_from_followed_sites_without_a_search_and_from_site_search_with_one()
    {
        _http.On(HttpMethod.Get, "/me/followedSites", HttpStatusCode.OK, "{\"value\":[{\"id\":\"s1\",\"displayName\":\"HR\",\"webUrl\":\"https://c.sharepoint.com/sites/hr\"}]}");
        _http.On(HttpMethod.Get, "/sites?search=hr", HttpStatusCode.OK, "{\"value\":[{\"id\":\"s2\",\"name\":\"hr-archive\"}]}");

        var followed = await _service.ListSitesAsync(new ListSitesRequest(), CancellationToken.None);
        var searched = await _service.ListSitesAsync(new ListSitesRequest { Search = "hr" }, CancellationToken.None);

        followed.Value!.Single().Should().BeEquivalentTo(new SiteSummary { SiteId = "s1", Name = "HR", WebUrl = "https://c.sharepoint.com/sites/hr" });
        searched.Value!.Single().Name.Should().Be("hr-archive", because: "name is the fallback when displayName is absent");
    }

    [Fact]
    public async Task AC16_an_off_graph_cursor_is_refused_before_any_call()
    {
        var cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("https://evil.example/x")).TrimEnd('=');

        var result = await _service.ListItemsAsync(new ListItemsRequest { Cursor = cursor }, CancellationToken.None);

        result.FailureKind.Should().Be(DriveFailureKind.InvalidRequest);
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Not_connected_is_passed_through_untouched()
    {
        _connections.Setup(c => c.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DriveResult<string>.Fail(DriveFailureKind.NotConnected, "no"));

        var result = await _service.ListItemsAsync(new ListItemsRequest { Source = "onedrive" }, CancellationToken.None);

        result.FailureKind.Should().Be(DriveFailureKind.NotConnected);
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AC19_create_folder_posts_name_folder_facet_and_fail_on_conflict()
    {
        _http.On(HttpMethod.Post, "/drives/drive-1/items/parent-1/children", HttpStatusCode.Created, DriveTestSupport.Item("new-1", "Signed", folder: true));

        var result = await _service.CreateFolderAsync(new CreateFolderRequest { Source = "onedrive", DriveId = "drive-1", ParentId = "parent-1", Name = "Signed" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ItemId.Should().Be("new-1");
        result.Value.Kind.Should().Be("folder");
        var body = _http.Requests.Single().Body!;
        body.Should().Contain("\"name\":\"Signed\"");
        body.Should().Contain("\"folder\":{}");
        body.Should().Contain("\"@microsoft.graph.conflictBehavior\":\"fail\"", because: "a silently created 'Signed (1)' is worse than a 409");
    }

    [Fact]
    public async Task AC20_an_existing_folder_name_is_a_name_conflict()
    {
        _http.On(HttpMethod.Post, "/children", HttpStatusCode.Conflict, "{\"error\":{\"code\":\"nameAlreadyExists\",\"message\":\"The name exists\"}}");

        var result = await _service.CreateFolderAsync(new CreateFolderRequest { Source = "onedrive", DriveId = "drive-1", Name = "Signed" }, CancellationToken.None);

        result.FailureKind.Should().Be(DriveFailureKind.NameConflict);
    }

    [Fact]
    public async Task An_invalid_folder_name_never_reaches_graph()
    {
        var result = await _service.CreateFolderAsync(new CreateFolderRequest { Source = "onedrive", DriveId = "drive-1", Name = "bad:name" }, CancellationToken.None);

        result.FailureKind.Should().Be(DriveFailureKind.InvalidRequest);
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AC21_import_streams_the_download_into_storage_and_returns_the_new_file_id()
    {
        var bytes = new byte[3 * 1024 * 1024];
        _http.On(HttpMethod.Get, "/drives/drive-1/items/item-1", HttpStatusCode.OK,
            "{\"id\":\"item-1\",\"name\":\"nda.pdf\",\"size\":3145728,\"file\":{\"mimeType\":\"application/pdf\"},\"@microsoft.graph.downloadUrl\":\"https://cdn.sharepoint.example/dl/item-1?tempauth=x\"}");
        _http.On(HttpMethod.Get, "cdn.sharepoint.example/dl/item-1", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BufferedStream(new MemoryStream(bytes))) });

        long? savedLength = null;
        string? savedName = null;
        _storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Stream>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string?, Stream, long?, CancellationToken>((_, name, _, stream, length, _) =>
            {
                savedName = name;
                savedLength = length;
                stream.Should().NotBeOfType<MemoryStream>(because: "the Graph response stream is passed through, not buffered");
            })
            .ReturnsAsync(true);

        var result = await _service.ImportAsync(new ImportFileRequest { DriveId = "drive-1", ItemId = "item-1" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FileId.Should().NotBeNullOrEmpty();
        result.Value.Name.Should().Be("nda.pdf");
        result.Value.MimeType.Should().Be("application/pdf");
        savedName.Should().Be("nda.pdf");
        savedLength.Should().Be(bytes.Length);
        _http.Requests[1].Message.Headers.Authorization.Should().BeNull(because: "the download URL is pre-authenticated and on another host");
    }

    [Fact]
    public async Task AC22_an_oversized_drive_file_is_refused_before_any_download()
    {
        _http.On(HttpMethod.Get, "/drives/drive-1/items/big", HttpStatusCode.OK,
            "{\"id\":\"big\",\"name\":\"big.zip\",\"size\":99999999,\"file\":{},\"@microsoft.graph.downloadUrl\":\"https://cdn/dl\"}");

        var result = await _service.ImportAsync(new ImportFileRequest { DriveId = "drive-1", ItemId = "big" }, CancellationToken.None);

        result.FailureKind.Should().Be(DriveFailureKind.FileTooLarge);
        _http.Requests.Should().HaveCount(1, because: "no bytes are moved for a file that cannot be stored");
        _storage.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Stream>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AC23_a_storage_failure_during_import_is_a_storage_error_with_no_file_id()
    {
        _http.On(HttpMethod.Get, "/drives/drive-1/items/item-1", HttpStatusCode.OK,
            "{\"id\":\"item-1\",\"name\":\"nda.pdf\",\"size\":10,\"file\":{},\"@microsoft.graph.downloadUrl\":\"https://cdn/dl\"}");
        _http.On(HttpMethod.Get, "https://cdn/dl", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) });
        _storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Stream>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.ImportAsync(new ImportFileRequest { DriveId = "drive-1", ItemId = "item-1" }, CancellationToken.None);

        result.FailureKind.Should().Be(DriveFailureKind.StorageError);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task AC24_AC27_small_files_use_a_simple_put_with_rename_and_report_the_name_graph_chose()
    {
        OpenStorageFile("file-a", "signed.zip", 1024 * 1024);
        OpenStorageFile("file-b", "signed.zip", 1024 * 1024);
        var names = new Queue<string>(["signed.zip", "signed (1).zip"]);
        _http.On(HttpMethod.Put, ":/signed.zip:/content", _ => ScriptedHttpHandler.Json(HttpStatusCode.Created, DriveTestSupport.Item("up", names.Dequeue(), size: 1048576)));

        var result = await _service.UploadAsync(new UploadFilesRequest { Source = "onedrive", DriveId = "drive-1", ParentId = "folder-1", FileIds = ["file-a", "file-b"] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
        result.Value.Results[1].Item!.Name.Should().Be("signed (1).zip", because: "Graph's rename is what the caller must show the user");
        _http.Requests.Should().HaveCount(2);
        _http.Requests[0].Url.Should().Be($"{Graph}/drives/drive-1/items/folder-1:/signed.zip:/content?@microsoft.graph.conflictBehavior=rename");
        _http.Requests[0].Message.Content!.Headers.ContentLength.Should().Be(1024 * 1024);
    }

    [Fact]
    public async Task AC25_a_large_file_goes_through_an_upload_session_in_aligned_chunks_with_content_ranges()
    {
        const int size = (4 * 1024 * 1024) + 100;
        OpenStorageFile("file-big", "scan.pdf", size);
        _http.On(HttpMethod.Post, ":/scan.pdf:/createUploadSession", HttpStatusCode.OK, "{\"uploadUrl\":\"https://upload.graph.example/session/1\"}");
        _http.On(HttpMethod.Put, "upload.graph.example/session/1", request =>
        {
            var range = request.Content!.Headers.ContentRange!;
            return range.To == size - 1
                ? ScriptedHttpHandler.Json(HttpStatusCode.Created, DriveTestSupport.Item("done", "scan.pdf", size: size))
                : ScriptedHttpHandler.Json(HttpStatusCode.Accepted, "{\"nextExpectedRanges\":[\"x\"]}");
        });

        var result = await _service.UploadAsync(new UploadFilesRequest { Source = "onedrive", DriveId = "drive-1", FileIds = ["file-big"] }, CancellationToken.None);

        result.Value!.Results.Single().Success.Should().BeTrue();
        result.Value.Results.Single().Item!.ItemId.Should().Be("done");

        var session = _http.Requests[0];
        session.Url.Should().Be($"{Graph}/drives/drive-1/root:/scan.pdf:/createUploadSession");
        session.Body.Should().Contain("\"@microsoft.graph.conflictBehavior\":\"rename\"");

        var chunks = _http.Requests.Skip(1).ToList();
        const int chunk = 320 * 1024;
        chunks.Should().HaveCount((int)Math.Ceiling(size / (double)chunk));
        for (var i = 0; i < chunks.Count; i++)
        {
            var range = chunks[i].Message.Content!.Headers.ContentRange!;
            range.From.Should().Be((long)i * chunk);
            range.To.Should().Be(Math.Min(((long)(i + 1) * chunk) - 1, size - 1));
            range.Length.Should().Be(size);
            chunks[i].Message.Headers.Authorization.Should().BeNull(because: "Graph rejects a bearer header on a session URL");
        }
    }

    [Fact]
    public async Task AC26_a_failing_file_does_not_stop_the_others_and_is_reported_per_file()
    {
        OpenStorageFile("ok-1", "a.pdf", 10);
        OpenStorageFile("bad", "b.pdf", 10);
        OpenStorageFile("ok-2", "c.pdf", 10);
        _http.On(HttpMethod.Put, ":/a.pdf:/content", HttpStatusCode.Created, DriveTestSupport.Item("a", "a.pdf"));
        _http.On(HttpMethod.Put, ":/b.pdf:/content", HttpStatusCode.InternalServerError, "{\"error\":{\"code\":\"generalException\",\"message\":\"boom\"}}");
        _http.On(HttpMethod.Put, ":/c.pdf:/content", HttpStatusCode.Created, DriveTestSupport.Item("c", "c.pdf"));

        var result = await _service.UploadAsync(new UploadFilesRequest { Source = "onedrive", DriveId = "drive-1", FileIds = ["ok-1", "bad", "ok-2"] }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue(because: "partial failure is reported per file, not as a failed request");
        result.Value!.Results.Select(r => r.Success).Should().Equal(true, false, true);
        result.Value.Results[1].Error.Should().StartWith("ProviderError");
    }

    [Fact]
    public async Task A_missing_storage_file_is_a_per_file_storage_error()
    {
        _storage.Setup(s => s.OpenAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((StorageFileHandle?)null);

        var result = await _service.UploadAsync(new UploadFilesRequest { Source = "onedrive", DriveId = "drive-1", FileIds = ["missing"] }, CancellationToken.None);

        result.Value!.Results.Single().Error.Should().StartWith("StorageError");
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AC28_too_many_file_ids_fail_validation()
    {
        var result = await _service.UploadAsync(new UploadFilesRequest { Source = "onedrive", DriveId = "drive-1", FileIds = Enumerable.Range(0, 21).Select(i => $"f{i}").ToList() }, CancellationToken.None);

        result.FailureKind.Should().Be(DriveFailureKind.InvalidRequest);
        _http.Requests.Should().BeEmpty();
    }

    private void OpenStorageFile(string fileId, string name, int size) =>
        _storage.Setup(s => s.OpenAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new StorageFileHandle { Name = name, Content = new MemoryStream(new byte[size]), Length = size });
}
