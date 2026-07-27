namespace Payment.DomainService.Responses;

public sealed class CursorPageInfoResponse
{
    public int PageSize { get; init; }
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage { get; init; }
    public string? StartCursor { get; init; }
    public string? EndCursor { get; init; }
}
