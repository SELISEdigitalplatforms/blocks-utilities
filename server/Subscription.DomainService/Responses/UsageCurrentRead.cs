namespace Subscription.DomainService.Responses;

/// <summary>
/// One current-usage read: the usage, and how it was served.
/// </summary>
public sealed class UsageCurrentRead
{
    public IReadOnlyList<UsageResponse> Items { get; init; } = [];

    public UsageReadDiagnostics Diagnostics { get; init; } = new();
}
