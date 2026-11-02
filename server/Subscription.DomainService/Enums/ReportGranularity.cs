namespace Subscription.DomainService.Enums;

/// <summary>
/// The time bucket a report is folded into: usage, billed revenue, or the recurring revenue trend.
/// </summary>
/// <remarks>
/// Weeks start on Monday (ISO 8601) and, like months, are aligned to the calendar rather than to
/// the window's start, so two windows that overlap the same week report the same bucket start.
/// </remarks>
public enum ReportGranularity
{
    Day,
    Week,
    Month
}
