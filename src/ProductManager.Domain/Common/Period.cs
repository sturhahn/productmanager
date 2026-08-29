namespace ProductManager.Domain.Common;

/// <summary>
/// A planning period (calendar month). The unit of capacity budgeting and effort demand.
/// Immutable value object, totally ordered.
/// </summary>
public sealed record Period : IComparable<Period>
{
    public int Year { get; }
    public int Month { get; }

    private Period() { } // EF

    public Period(int year, int month)
    {
        if (year is < 2000 or > 2200)
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be between 2000 and 2200.");
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        Year = year;
        Month = month;
    }

    public static Period FromDate(DateOnly date) => new(date.Year, date.Month);

    public Period Next() => Month == 12 ? new Period(Year + 1, 1) : new Period(Year, Month + 1);

    public int CompareTo(Period? other)
    {
        if (other is null) return 1;
        var byYear = Year.CompareTo(other.Year);
        return byYear != 0 ? byYear : Month.CompareTo(other.Month);
    }

    /// <summary>All periods from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    public static IEnumerable<Period> Range(Period from, Period to)
    {
        if (from.CompareTo(to) > 0)
            throw new ArgumentException("Range start must not be after range end.");
        for (var p = from; p.CompareTo(to) <= 0; p = p.Next())
            yield return p;
    }

    public override string ToString() => $"{Year:0000}-{Month:00}";
}
