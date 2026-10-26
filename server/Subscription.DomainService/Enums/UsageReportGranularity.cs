namespace Subscription.DomainService.Enums;

/// <summary>
/// The time bucket a usage report is folded into.
/// </summary>
/// <remarks>
/// Weeks start on Monday (ISO 8601) and, like months, are aligned to the calendar rather than to
/// the window's start, so two windows that overlap the same week report the same bucket start.
/// </remarks>
public enum UsageReportGranularity
{
    Day,
    Week,
    Month
}
