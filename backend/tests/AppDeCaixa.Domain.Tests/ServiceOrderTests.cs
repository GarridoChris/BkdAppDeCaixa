using AppDeCaixa.Domain;
using AppDeCaixa.Domain.ServiceOrders;

namespace AppDeCaixa.Domain.Tests;

public sealed class ServiceOrderTests
{
    [Fact]
    public void Added_service_increments_total_using_whole_reais()
    {
        var order = ServiceOrder.Open(1, 9, 12);

        var firstTotal = order.AddService(Money.FromWholeReais(120), commissionPercentage: 40);
        var secondTotal = order.AddService(Money.FromWholeReais(80), commissionPercentage: 50);

        Assert.Equal(120, firstTotal.WholeReais);
        Assert.Equal(200, secondTotal.WholeReais);
        Assert.Equal(1, order.CompanyId);
        Assert.Equal(9, order.CashSessionId);
    }

    [Fact]
    public void Adding_service_rejects_negative_price_and_invalid_commission()
    {
        var order = ServiceOrder.Open(1, 9, 12);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.AddService(Money.FromWholeReais(-1), commissionPercentage: 40));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.AddService(Money.FromWholeReais(100), commissionPercentage: 101));
    }

    [Fact]
    public void Order_total_cannot_overflow_the_database_integer_range()
    {
        var order = ServiceOrder.Open(1, 9, 12);
        order.AddService(Money.FromWholeReais(int.MaxValue), commissionPercentage: 100);

        Assert.Throws<OverflowException>(() =>
            order.AddService(Money.FromWholeReais(1), commissionPercentage: 100));
    }
}
