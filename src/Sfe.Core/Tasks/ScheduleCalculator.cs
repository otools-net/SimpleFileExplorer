namespace Sfe.Core.Tasks;

/// <summary>When a schedule comes due. Pure functions of the schedule, a moment and a time zone, for tests.</summary>
public static class ScheduleCalculator
{
    /// <summary>Every day of the week, as <see cref="TaskScheduleInput.Days"/>.</summary>
    public const int AllDays = 0b111_1111;

    /// <summary>
    /// First occurrence strictly after <paramref name="afterUtc"/>, or null when there is none (a one-off already
    /// past, a weekly schedule without days).
    /// </summary>
    /// <param name="zone">Time zone whose clock weekly schedules follow (the server's).</param>
    public static DateTime? Next(ScheduleKind kind, DateTime startUtc, int intervalMinutes, int days, DateTime afterUtc, TimeZoneInfo zone)
    {
        startUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        afterUtc = DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc);
        switch (kind)
        {
            case ScheduleKind.Once:
                return startUtc > afterUtc ? startUtc : null;

            case ScheduleKind.Interval:
                if (intervalMinutes <= 0)
                    return null;
                if (startUtc > afterUtc)
                    return startUtc;
                var interval = TimeSpan.FromMinutes(intervalMinutes);
                var elapsed = (afterUtc - startUtc).Ticks / interval.Ticks + 1;
                return startUtc + interval * elapsed;

            case ScheduleKind.Weekly:
                return NextWeekly(startUtc, days & AllDays, afterUtc, zone);

            default:
                return null;
        }
    }

    /// <summary>The next <paramref name="count"/> occurrences after <paramref name="afterUtc"/>.</summary>
    public static List<DateTime> NextRuns(ScheduleKind kind, DateTime startUtc, int intervalMinutes, int days, DateTime afterUtc, TimeZoneInfo zone, int count)
    {
        var runs = new List<DateTime>(count);
        var after = afterUtc;
        while (runs.Count < count && Next(kind, startUtc, intervalMinutes, days, after, zone) is { } next)
        {
            runs.Add(next);
            after = next;
        }

        return runs;
    }

    private static DateTime? NextWeekly(DateTime startUtc, int days, DateTime afterUtc, TimeZoneInfo zone)
    {
        if (days == 0)
            return null;

        var timeOfDay = TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone).TimeOfDay;
        var from = afterUtc > startUtc ? afterUtc : startUtc;
        var date = TimeZoneInfo.ConvertTimeFromUtc(from, zone).Date;
        // A week and a day: the time may already be past on the first day, and a clock change can move it.
        for (var i = 0; i <= 8; i++, date = date.AddDays(1))
        {
            if ((days & (1 << (int)date.DayOfWeek)) == 0)
                continue;
            var candidate = ToUtc(date + timeOfDay, zone);
            if (candidate > afterUtc && candidate >= startUtc)
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// A wall-clock time of <paramref name="zone"/> in UTC. A time skipped by the clock moving forward runs that much
    /// later (02:30 on the spring night is 03:30); a time that happens twice runs the first time.
    /// </summary>
    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            var gap = zone.GetAdjustmentRules()
                .FirstOrDefault(r => r.DateStart <= local && local <= r.DateEnd)?.DaylightDelta ?? TimeSpan.FromHours(1);
            local += gap;
        }

        if (zone.IsAmbiguousTime(local))
        {
            // The larger offset is the earlier of the two moments.
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
