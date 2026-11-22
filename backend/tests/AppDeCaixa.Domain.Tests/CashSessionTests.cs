using AppDeCaixa.Domain;
using AppDeCaixa.Domain.CashSessions;

namespace AppDeCaixa.Domain.Tests;

public sealed class CashSessionTests
{
    [Fact]
    public void New_session_starts_open_and_keeps_opening_details()
    {
        var session = CashSession.Open(1, 7, new DateOnly(2026, 10, 29), Money.FromWholeReais(200));

        Assert.Equal(CashSessionStatus.Open, session.Status);
        Assert.Equal(1, session.CompanyId);
        Assert.Equal(7, session.OpenedByUserId);
        Assert.Equal(200, session.OpeningCash.WholeReais);
    }

    [Fact]
    public void Closing_session_records_cash_and_actor()
    {
        var session = CashSession.Open(1, 7, new DateOnly(2026, 10, 29), Money.FromWholeReais(200));

        session.Close(8, new DateOnly(2026, 10, 29), Money.FromWholeReais(350));

        Assert.Equal(CashSessionStatus.Closed, session.Status);
        Assert.Equal(8, session.ClosedByUserId);
        Assert.Equal(350, session.ClosingCash?.WholeReais);
    }

    [Fact]
    public void Closed_session_cannot_be_closed_again()
    {
        var session = CashSession.Open(1, 7, new DateOnly(2026, 10, 29), Money.Zero);
        session.Close(7, new DateOnly(2026, 10, 29), Money.Zero);

        Assert.Throws<InvalidOperationException>(() =>
            session.Close(7, new DateOnly(2026, 10, 29), Money.Zero));
    }

    [Fact]
    public void Session_cannot_open_with_negative_cash()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CashSession.Open(1, 7, new DateOnly(2026, 10, 29), Money.FromWholeReais(-1)));
    }

    [Fact]
    public void Session_cannot_close_before_its_opening_business_date()
    {
        var session = CashSession.Open(1, 7, new DateOnly(2026, 10, 29), Money.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            session.Close(7, new DateOnly(2026, 10, 28), Money.Zero));
    }
}
