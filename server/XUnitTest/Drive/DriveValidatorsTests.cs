using FluentAssertions;
using Utility.DomainService.Drive.Models;
using Utility.DomainService.Drive.Validators;

namespace XUnitTest.Drive;

/// <summary>
/// Guards the request rules. A listing with a bogus sort field or a 10,000 page size would
/// otherwise reach Graph and come back as an opaque 400.
/// </summary>
public sealed class DriveValidatorsTests
{
    private readonly ListItemsRequestValidator _list = new(DriveTestSupport.Options());
    private readonly UploadFilesRequestValidator _upload = new(DriveTestSupport.Options());
    private readonly CreateFolderRequestValidator _folder = new();

    [Theory]
    [InlineData("onedrive", null, null, "name", "asc", 50, true)]
    [InlineData("sharepoint", "site-1", null, null, null, null, true)]
    [InlineData("sharepoint", null, "drive-1", null, null, null, true)]
    [InlineData("sharepoint", null, null, null, null, null, false)]
    [InlineData("dropbox", null, null, null, null, null, false)]
    [InlineData("onedrive", null, null, "colour", null, null, false)]
    [InlineData("onedrive", null, null, "name", "sideways", null, false)]
    [InlineData("onedrive", null, null, null, null, 0, false)]
    [InlineData("onedrive", null, null, null, null, 201, false)]
    public void Listing_rules(string source, string? siteId, string? driveId, string? sortBy, string? sortOrder, int? pageSize, bool valid)
    {
        var result = _list.Validate(new ListItemsRequest { Source = source, SiteId = siteId, DriveId = driveId, SortBy = sortBy, SortOrder = sortOrder, PageSize = pageSize });

        result.IsValid.Should().Be(valid, because: string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void A_cursor_alone_is_enough_to_continue_a_listing()
    {
        _list.Validate(new ListItemsRequest { Cursor = "abc" }).IsValid.Should().BeTrue(
            because: "the cursor already encodes drive, folder and search");
    }

    [Theory]
    [InlineData("Signed", true)]
    [InlineData("Signed 2026", true)]
    [InlineData("", false)]
    [InlineData("a:b", false)]
    [InlineData("..", false)]
    [InlineData("trailing.", false)]
    public void Folder_names_follow_graph_rules(string name, bool valid)
    {
        _folder.Validate(new CreateFolderRequest { Source = "onedrive", Name = name }).IsValid.Should().Be(valid);
    }

    [Fact]
    public void Uploads_need_between_one_and_twenty_non_empty_file_ids()
    {
        _upload.Validate(new UploadFilesRequest { Source = "onedrive", FileIds = [] }).IsValid.Should().BeFalse();
        _upload.Validate(new UploadFilesRequest { Source = "onedrive", FileIds = ["a", " "] }).IsValid.Should().BeFalse();
        _upload.Validate(new UploadFilesRequest { Source = "onedrive", FileIds = Enumerable.Range(0, 21).Select(i => $"f{i}").ToList() }).IsValid.Should().BeFalse();
        _upload.Validate(new UploadFilesRequest { Source = "onedrive", FileIds = ["a"] }).IsValid.Should().BeTrue();
    }
}
