namespace Subscription.DomainService.Utilities;

/// <summary>
/// Converts a billing boundary expressed in a customer's local calendar into an instant.
/// </summary>
/// <remarks>
/// Twice a year a local time either does not exist or exists twice, and a boundary that lands
/// there has to resolve to exactly one instant. Left to the framework both cases throw, which
/// would take out a renewal on precisely two nights a year and look like an unrelated outage.
/// </remarks>
public static class BillingLocalTime
{
    /// <summary>How far forward a boundary may be nudged to escape a spring-forward gap.</summary>
    private static readonly TimeSpan MaximumGapSearch = TimeSpan.FromHours(4);

    private static readonly TimeSpan GapStep = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Resolves an IANA identifier. Returns false rather than throwing, so a misconfigured
    /// subscription fails closed at its own boundary instead of taking down the caller.
    /// </summary>
    /// <remarks>
    /// The container images must carry <c>tzdata</c>; without it every identifier fails here,
    /// on Alpine, in production only.
    /// </remarks>
    public static bool TryFindTimeZone(string? timeZoneId, out TimeZoneInfo timeZone)
    {
        timeZone = TimeZoneInfo.Utc;

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return false;
        }

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>
    /// Turns a local billing boundary into the instant it happens at.
    /// </summary>
    /// <remarks>
    /// A boundary inside a spring-forward gap moves to the first local time that exists, so the
    /// period starts rather than being skipped. An autumn boundary that happens twice takes the
    /// earlier of the two: which one matters far less than always picking the same one, since
    /// an inconsistent choice would make a period overlap or gap by an hour.
    /// </remarks>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(unspecified))
        {
            unspecified = FirstValidTimeAtOrAfter(unspecified, timeZone);
        }

        if (timeZone.IsAmbiguousTime(unspecified))
        {
            // The larger offset is the pre-transition one, so subtracting it gives the earlier
            // of the two instants that share this local time.
            var earliest = timeZone
                .GetAmbiguousTimeOffsets(unspecified)
                .Max();

            return DateTime.SpecifyKind(unspecified - earliest, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
    }

    /// <summary>Turns an instant into the local calendar time it falls on.</summary>
    public static DateTime ToLocal(DateTime instantUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        return TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(instantUtc, DateTimeKind.Utc),
            timeZone);
    }

    private static DateTime FirstValidTimeAtOrAfter(
        DateTime local,
        TimeZoneInfo timeZone)
    {
        var searched = TimeSpan.Zero;
        var candidate = local;

        while (timeZone.IsInvalidTime(candidate) && searched < MaximumGapSearch)
        {
            candidate = candidate.Add(GapStep);
            searched = searched.Add(GapStep);
        }

        // Stepping rather than reading the adjustment rule keeps this correct for gaps of any
        // size, including the half-hour ones some zones use.
        return timeZone.IsInvalidTime(candidate) ? local : candidate;
    }
}
