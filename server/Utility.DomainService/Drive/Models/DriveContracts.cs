namespace Utility.DomainService.Drive.Models;

/// <summary>Which drive a request addresses. OneDrive and SharePoint share one Graph item model;
/// this is the only place they differ.</summary>
public static class DriveSource
{
    public const string OneDrive = "onedrive";
    public const string SharePoint = "sharepoint";
}

public sealed class StartConnectionRequest
{
    /// <summary>Where the browser lands after Entra. Must be on one of the project's domains.</summary>
    public string? ReturnUrl { get; set; }
}

public sealed class StartConnectionResponse
{
    /// <summary>Set when the user must be sent to Entra. Null when already connected.</summary>
    public string? AuthorizeUrl { get; set; }

    public bool Connected { get; set; }
}

public sealed class ConnectionStatusResponse
{
    public bool Connected { get; set; }

    public string? AccountDisplayName { get; set; }

    public string? AccountEmail { get; set; }

    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// Always present so a caller that got <c>admin_consent_required</c> has something to show
    /// the user's IT admin without a second round trip.
    /// </summary>
    public string? AdminConsentUrl { get; set; }
}

/// <summary>
/// What the callback decided, for the controller to turn into a redirect. <see cref="RedirectUrl"/>
/// is null only when there is nowhere safe to send the browser (unknown or expired state).
/// </summary>
public sealed class ConnectionCallbackOutcome
{
    public string? RedirectUrl { get; set; }

    public string Status { get; set; } = string.Empty;

    public const string Connected = "connected";
    public const string Failed = "failed";
    public const string AdminConsentRequired = "admin_consent_required";
}

public sealed class SiteSummary
{
    public string SiteId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? WebUrl { get; set; }
}

public sealed class DriveItemSummary
{
    public string DriveId { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary><c>file</c> or <c>folder</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public long Size { get; set; }

    public string? MimeType { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public string? WebUrl { get; set; }

    public string? ParentId { get; set; }
}

public sealed class ListSitesRequest
{
    public string? Search { get; set; }
}

public sealed class ListItemsRequest
{
    public string? Source { get; set; }

    public string? SiteId { get; set; }

    public string? DriveId { get; set; }

    /// <summary>Absent means the drive root.</summary>
    public string? ParentId { get; set; }

    public string? Search { get; set; }

    public string? SortBy { get; set; }

    public string? SortOrder { get; set; }

    public int? PageSize { get; set; }

    public string? Cursor { get; set; }
}

public sealed class ItemsPage
{
    public IReadOnlyList<DriveItemSummary> Items { get; set; } = [];

    public string? NextCursor { get; set; }

    /// <summary>False when a search was requested: Graph cannot sort search results.</summary>
    public bool SortApplied { get; set; } = true;
}

public sealed class CreateFolderRequest
{
    public string? Source { get; set; }

    public string? SiteId { get; set; }

    public string? DriveId { get; set; }

    public string? ParentId { get; set; }

    public string? Name { get; set; }
}

public sealed class ImportFileRequest
{
    public string? DriveId { get; set; }

    public string? ItemId { get; set; }
}

public sealed class ImportFileResponse
{
    public string FileId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long Size { get; set; }

    public string? MimeType { get; set; }
}

public sealed class UploadFilesRequest
{
    public string? Source { get; set; }

    public string? SiteId { get; set; }

    public string? DriveId { get; set; }

    public string? ParentId { get; set; }

    public List<string>? FileIds { get; set; }
}

public sealed class UploadFileResult
{
    public string FileId { get; set; } = string.Empty;

    public bool Success { get; set; }

    public DriveItemSummary? Item { get; set; }

    public string? Error { get; set; }
}

public sealed class UploadFilesResponse
{
    public IReadOnlyList<UploadFileResult> Results { get; set; } = [];
}
