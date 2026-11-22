namespace AppDeCaixa.Domain.ServiceOrders;

public sealed class ServiceOrder
{
    private ServiceOrder(int companyId, int cashSessionId, int openedByUserId, Money total)
    {
        CompanyId = companyId;
        CashSessionId = cashSessionId;
        OpenedByUserId = openedByUserId;
        Total = total;
    }

    public int CompanyId { get; }
    public int CashSessionId { get; }
    public int OpenedByUserId { get; }
    public Money Total { get; private set; }

    public static ServiceOrder Open(int companyId, int cashSessionId, int openedByUserId) =>
        RestoreOpen(companyId, cashSessionId, openedByUserId, Money.Zero);

    public static ServiceOrder RestoreOpen(int companyId, int cashSessionId, int openedByUserId, Money total)
    {
        if (companyId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(companyId));
        }

        if (cashSessionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cashSessionId));
        }

        if (openedByUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openedByUserId));
        }

        if (total.WholeReais < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total), "Order total cannot be negative.");
        }

        return new ServiceOrder(companyId, cashSessionId, openedByUserId, total);
    }

    public Money AddService(Money unitPrice, int commissionPercentage)
    {
        if (unitPrice.WholeReais < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Service price cannot be negative.");
        }

        if (commissionPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(commissionPercentage));
        }

        Total = Total.Add(unitPrice);
        return Total;
    }
}
