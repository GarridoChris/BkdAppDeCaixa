namespace AppDeCaixa.Domain.CashSessions;

public enum CashSessionStatus
{
    Open,
    Closed
}

public sealed class CashSession
{
    private CashSession(
        int companyId,
        int openedByUserId,
        DateOnly businessDate,
        Money openingCash)
    {
        CompanyId = companyId;
        OpenedByUserId = openedByUserId;
        BusinessDate = businessDate;
        OpeningCash = openingCash;
        Status = CashSessionStatus.Open;
    }

    public int? Id { get; private set; }
    public int CompanyId { get; }
    public int OpenedByUserId { get; }
    public DateOnly BusinessDate { get; }
    public Money OpeningCash { get; }
    public CashSessionStatus Status { get; private set; }
    public int? ClosedByUserId { get; private set; }
    public DateOnly? ClosedBusinessDate { get; private set; }
    public Money? ClosingCash { get; private set; }

    public static CashSession Open(
        int companyId,
        int openedByUserId,
        DateOnly businessDate,
        Money openingCash)
    {
        if (companyId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(companyId));
        }

        if (openedByUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openedByUserId));
        }

        if (openingCash.WholeReais < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(openingCash), "Opening cash cannot be negative.");
        }

        return new CashSession(companyId, openedByUserId, businessDate, openingCash);
    }

    public void Close(int closedByUserId, DateOnly closedBusinessDate, Money countedCash)
    {
        if (Status != CashSessionStatus.Open)
        {
            throw new InvalidOperationException("Only an open cash session can be closed.");
        }

        if (closedByUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(closedByUserId));
        }

        if (closedBusinessDate < BusinessDate)
        {
            throw new ArgumentOutOfRangeException(nameof(closedBusinessDate), "Closing date cannot be before the opening date.");
        }

        if (countedCash.WholeReais < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(countedCash), "Counted cash cannot be negative.");
        }

        ClosedByUserId = closedByUserId;
        ClosedBusinessDate = closedBusinessDate;
        ClosingCash = countedCash;
        Status = CashSessionStatus.Closed;
    }
}
