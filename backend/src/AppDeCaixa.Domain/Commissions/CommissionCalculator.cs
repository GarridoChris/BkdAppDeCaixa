using AppDeCaixa.Domain;

namespace AppDeCaixa.Domain.Commissions;

public static class CommissionCalculator
{
    public static Money Calculate(Money chargedServiceAmount, int commissionPercentage)
    {
        if (chargedServiceAmount.WholeReais < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chargedServiceAmount));
        }

        if (commissionPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(commissionPercentage));
        }

        var percentageProduct = checked((long)chargedServiceAmount.WholeReais * commissionPercentage);
        var roundedWholeReais = checked((percentageProduct + 50) / 100);
        return Money.FromWholeReais(roundedWholeReais);
    }
}
