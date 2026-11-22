using AppDeCaixa.Domain;

namespace AppDeCaixa.Domain.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Whole_real_amount_is_preserved_exactly()
    {
        var amount = Money.FromWholeReais(125m);

        Assert.Equal(125, amount.WholeReais);
    }

    [Fact]
    public void Fractional_real_amount_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.FromWholeReais(10.50m));
    }

    [Fact]
    public void Addition_and_subtraction_use_integer_arithmetic()
    {
        var total = Money.FromWholeReais(125m).Add(Money.FromWholeReais(7m));

        Assert.Equal(132, total.WholeReais);
        Assert.Equal(118, total.Subtract(Money.FromWholeReais(14m)).WholeReais);
    }
}
