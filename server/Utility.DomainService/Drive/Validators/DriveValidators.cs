using FluentValidation;
using Microsoft.Extensions.Options;
using Utility.DomainService.Drive.Models;

namespace Utility.DomainService.Drive.Validators;

/// <summary>
/// Rules shared by every request that names a drive: a known source, and a site when the source
/// is SharePoint and no drive id short-circuits the lookup.
/// </summary>
internal static class DriveTargetRules
{
    public static readonly string[] Sources = [DriveSource.OneDrive, DriveSource.SharePoint];

    public static readonly string[] SortFields = ["name", "lastModifiedDateTime", "size"];

    public static readonly string[] SortOrders = ["asc", "desc"];

    // Graph's own list for OneDrive and SharePoint names.
    private static readonly char[] InvalidNameChars = ['"', '*', ':', '<', '>', '?', '/', '\\', '|'];

    public static bool IsSource(string? value) =>
        value is not null && Sources.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static bool IsSharePoint(string? value) =>
        string.Equals(value, DriveSource.SharePoint, StringComparison.OrdinalIgnoreCase);

    public static bool IsValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 255
        && name.IndexOfAny(InvalidNameChars) < 0
        && name.Trim() is not ("." or "..")
        && !name.EndsWith('.');
}

public sealed class StartConnectionRequestValidator : AbstractValidator<StartConnectionRequest>
{
    public StartConnectionRequestValidator()
    {
        RuleFor(x => x.ReturnUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("returnUrl must be an absolute URL.");
    }
}

public sealed class ListItemsRequestValidator : AbstractValidator<ListItemsRequest>
{
    public ListItemsRequestValidator(IOptions<DriveOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var maxPageSize = options.Value.MaxPageSize;

        When(x => string.IsNullOrEmpty(x.Cursor), () =>
        {
            RuleFor(x => x.Source)
                .Must(DriveTargetRules.IsSource)
                .WithMessage("source must be 'onedrive' or 'sharepoint'.");

            RuleFor(x => x.SiteId)
                .NotEmpty()
                .When(x => DriveTargetRules.IsSharePoint(x.Source) && string.IsNullOrEmpty(x.DriveId))
                .WithMessage("siteId is required for a SharePoint listing without a driveId.");
        });

        RuleFor(x => x.SortBy)
            .Must(value => DriveTargetRules.SortFields.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrEmpty(x.SortBy))
            .WithMessage("sortBy must be one of name, lastModifiedDateTime, size.");

        RuleFor(x => x.SortOrder)
            .Must(value => DriveTargetRules.SortOrders.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrEmpty(x.SortOrder))
            .WithMessage("sortOrder must be asc or desc.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, maxPageSize)
            .When(x => x.PageSize.HasValue)
            .WithMessage($"pageSize must be between 1 and {maxPageSize}.");
    }
}

public sealed class CreateFolderRequestValidator : AbstractValidator<CreateFolderRequest>
{
    public CreateFolderRequestValidator()
    {
        RuleFor(x => x.Source).Must(DriveTargetRules.IsSource).WithMessage("source must be 'onedrive' or 'sharepoint'.");
        RuleFor(x => x.SiteId)
            .NotEmpty()
            .When(x => DriveTargetRules.IsSharePoint(x.Source) && string.IsNullOrEmpty(x.DriveId))
            .WithMessage("siteId is required for SharePoint without a driveId.");
        RuleFor(x => x.Name)
            .Must(DriveTargetRules.IsValidName)
            .WithMessage("name must be 1-255 characters and contain none of \" * : < > ? / \\ |.");
    }
}

public sealed class ImportFileRequestValidator : AbstractValidator<ImportFileRequest>
{
    public ImportFileRequestValidator()
    {
        RuleFor(x => x.DriveId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
    }
}

public sealed class UploadFilesRequestValidator : AbstractValidator<UploadFilesRequest>
{
    public UploadFilesRequestValidator(IOptions<DriveOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var max = options.Value.MaxUploadFilesPerRequest;

        RuleFor(x => x.Source).Must(DriveTargetRules.IsSource).WithMessage("source must be 'onedrive' or 'sharepoint'.");
        RuleFor(x => x.SiteId)
            .NotEmpty()
            .When(x => DriveTargetRules.IsSharePoint(x.Source) && string.IsNullOrEmpty(x.DriveId))
            .WithMessage("siteId is required for SharePoint without a driveId.");
        RuleFor(x => x.FileIds)
            .NotNull()
            .Must(ids => ids is { Count: > 0 } && ids.Count <= max && ids.All(id => !string.IsNullOrWhiteSpace(id)))
            .WithMessage($"fileIds must hold between 1 and {max} non-empty ids.");
    }
}
