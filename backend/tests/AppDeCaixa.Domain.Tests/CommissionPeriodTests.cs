using AppDeCaixa.Domain.Commissions;

namespace AppDeCaixa.Domain.Tests;

public sealed class CommissionPeriodTests
{
    [Theory]
    [InlineData(2026, 11, 1, CommissionPeriodPart.FirstHalf)]
    [InlineData(2026, 11, 15, CommissionPeriodPart.FirstHalf)]
    [InlineData(2026, 11, 16, CommissionPeriodPart.SecondHalf)]
    [InlineData(2026, 11, 30, CommissionPeriodPart.SecondHalf)]
    public void For_assigns_dates_to_the_correct_half_of_the_month(
        int year,
        int month,
        int day,
        CommissionPeriodPart expectedPart)
    {
        var period = CommissionPeriod.For(new DateOnly(year, month, day));

        Assert.Equal(expectedPart, period.Part);
        Assert.True(period.Contains(new DateOnly(year, month, day)));
    }

    [Fact]
    public void Second_half_ends_on_the_last_day_of_february_in_a_leap_year()
    {
        var period = CommissionPeriod.Create(2028, 2, CommissionPeriodPart.SecondHalf);

        Assert.Equal(new DateOnly(2028, 2, 16), period.StartDate);
        Assert.Equal(new DateOnly(2028, 2, 29), period.EndDate);
    }

    [Fact]
    public void Create_rejects_invalid_month_and_period_part()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CommissionPeriod.Create(2026, 13, CommissionPeriodPart.FirstHalf));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CommissionPeriod.Create(2026, 1, (CommissionPeriodPart)2));
    }
}

public sealed class CommissionCalculatorTests
{
    [Theory]
    [InlineData(100, 40, 40)]
    [InlineData(5, 10, 1)]
    [InlineData(1, 50, 1)]
    [InlineData(1, 49, 0)]
    [InlineData(100, 0, 0)]
    public void Calculate_rounds_each_service_item_to_the_nearest_whole_real(
        int chargedAmount,
        int percentage,
        int expected)
    {
        var actual = CommissionCalculator.Calculate(Money.FromWholeReais(chargedAmount), percentage);

        Assert.Equal(expected, actual.WholeReais);
    }

    [Fact]
    public void Calculate_rejects_a_negative_amount_or_invalid_percentage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CommissionCalculator.Calculate(Money.FromWholeReais(-1), 40));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CommissionCalculator.Calculate(Money.FromWholeReais(100), 101));
    }
}
