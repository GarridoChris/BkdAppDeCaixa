namespace AppDeCaixa.Domain.Commissions;

public enum CommissionPeriodPart
{
    FirstHalf,
    SecondHalf
}

public readonly record struct CommissionPeriod
{
    private CommissionPeriod(int year, int month, CommissionPeriodPart part)
    {
        Year = year;
        Month = month;
        Part = part;
    }

    public int Year { get; }
    public int Month { get; }
    public CommissionPeriodPart Part { get; }

    public DateOnly StartDate => Part == CommissionPeriodPart.FirstHalf
        ? new DateOnly(Year, Month, 1)
        : new DateOnly(Year, Month, 16);

    public DateOnly EndDate => Part == CommissionPeriodPart.FirstHalf
        ? new DateOnly(Year, Month, 15)
        : new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    public static CommissionPeriod Create(int year, int month, CommissionPeriodPart part)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        if (!Enum.IsDefined(part))
        {
            throw new ArgumentOutOfRangeException(nameof(part));
        }

        return new CommissionPeriod(year, month, part);
    }

    public static CommissionPeriod For(DateOnly serviceDate) =>
        new(serviceDate.Year, serviceDate.Month,
            serviceDate.Day <= 15 ? CommissionPeriodPart.FirstHalf : CommissionPeriodPart.SecondHalf);
}
