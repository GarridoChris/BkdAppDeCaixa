using AppDeCaixa.Application.CashSessions;
using AppDeCaixa.Domain.CashSessions;

namespace AppDeCaixa.Domain.Tests;

public sealed class CashSessionServiceTests
{
    private static readonly DateOnly OpeningDate = new(2026, 10, 30);

    [Fact]
    public async Task Open_creates_session_for_company_and_authenticated_user()
    {
        var repository = new FakeCashSessionRepository();
        var service = new CashSessionService(repository);

        var result = await service.OpenAsync(1, 12, OpeningDate, 250, CancellationToken.None);

        Assert.Equal(CashSessionOperationStatus.Success, result.Status);
        Assert.NotNull(result.Session);
        Assert.Equal(1, result.Session.CompanyId);
        Assert.Equal(12, result.Session.OpenedByUserId);
        Assert.Equal(250, result.Session.OpeningCash);
        Assert.Equal("Open", result.Session.Status);
    }

    [Fact]
    public async Task Open_rejects_negative_opening_cash()
    {
        var repository = new FakeCashSessionRepository();
        var service = new CashSessionService(repository);

        var result = await service.OpenAsync(1, 12, OpeningDate, -1, CancellationToken.None);

        Assert.Equal(CashSessionOperationStatus.ValidationError, result.Status);
        Assert.Empty(repository.Sessions);
    }

    [Fact]
    public async Task Open_rejects_second_open_session_for_same_company()
    {
        var repository = new FakeCashSessionRepository();
        var service = new CashSessionService(repository);
        await service.OpenAsync(1, 12, OpeningDate, 250, CancellationToken.None);

        var result = await service.OpenAsync(1, 12, OpeningDate, 300, CancellationToken.None);

        Assert.Equal(CashSessionOperationStatus.Conflict, result.Status);
        Assert.Single(repository.Sessions);
    }

    [Fact]
    public async Task Close_records_counted_cash_and_closing_user()
    {
        var repository = new FakeCashSessionRepository();
        var service = new CashSessionService(repository);
        var opened = await service.OpenAsync(1, 12, OpeningDate, 250, CancellationToken.None);

        var result = await service.CloseAsync(1, opened.Session!.Id, 15, OpeningDate, 420, CancellationToken.None);

        Assert.Equal(CashSessionOperationStatus.Success, result.Status);
        Assert.Equal(15, result.Session?.ClosedByUserId);
        Assert.Equal(420, result.Session?.ClosingCash);
        Assert.Equal("Closed", result.Session?.Status);
    }

    [Fact]
    public async Task Close_rejects_negative_count_and_date_before_opening()
    {
        var repository = new FakeCashSessionRepository();
        var service = new CashSessionService(repository);
        var opened = await service.OpenAsync(1, 12, OpeningDate, 250, CancellationToken.None);

        var negativeResult = await service.CloseAsync(1, opened.Session!.Id, 15, OpeningDate, -1, CancellationToken.None);
        var earlierDateResult = await service.CloseAsync(
            1, opened.Session.Id, 15, OpeningDate.AddDays(-1), 420, CancellationToken.None);

        Assert.Equal(CashSessionOperationStatus.ValidationError, negativeResult.Status);
        Assert.Equal(CashSessionOperationStatus.ValidationError, earlierDateResult.Status);
        Assert.Null(repository.Sessions.Single().ClosedOn);
    }

    [Fact]
    public async Task Session_from_another_company_is_not_retrievable_or_closable()
    {
        var repository = new FakeCashSessionRepository();
        var service = new CashSessionService(repository);
        var opened = await service.OpenAsync(1, 12, OpeningDate, 250, CancellationToken.None);

        var result = await service.CloseAsync(2, opened.Session!.Id, 15, OpeningDate, 420, CancellationToken.None);

        Assert.Equal(CashSessionOperationStatus.NotFound, result.Status);
        Assert.Null(repository.Sessions.Single().ClosedOn);
    }

    [Fact]
    public async Task Close_is_blocked_while_service_orders_are_open()
    {
        var repository = new FakeCashSessionRepository { HasOpenServiceOrders = true };
        var service = new CashSessionService(repository);
        var opened = await service.OpenAsync(1, 12, OpeningDate, 250, CancellationToken.None);

        var result = await service.CloseAsync(1, opened.Session!.Id, 15, OpeningDate, 420, CancellationToken.None);

        Assert.Equal(CashSessionOperationStatus.Conflict, result.Status);
        Assert.Contains("service orders", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(repository.Sessions.Single().ClosedOn);
    }

    private sealed class FakeCashSessionRepository : ICashSessionRepository
    {
        private int _nextId = 1;
        public List<CashSessionDetails> Sessions { get; } = [];
        public bool HasOpenServiceOrders { get; init; }

        public Task<CashSessionDetails?> GetCurrentAsync(int companyId, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(session =>
                session.CompanyId == companyId && session.ClosedOn is null));

        public Task<CashSessionDetails?> GetByIdAsync(int companyId, int sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.SingleOrDefault(session =>
                session.CompanyId == companyId && session.Id == sessionId));

        public Task<CashSessionDetails?> CreateAsync(CashSession session, CancellationToken cancellationToken)
        {
            if (Sessions.Any(existing => existing.CompanyId == session.CompanyId && existing.ClosedOn is null))
            {
                return Task.FromResult<CashSessionDetails?>(null);
            }

            var details = new CashSessionDetails(
                _nextId++,
                session.CompanyId,
                session.BusinessDate,
                null,
                session.OpenedByUserId,
                null,
                session.OpeningCash.WholeReais,
                null,
                session.Status.ToString());
            Sessions.Add(details);
            return Task.FromResult<CashSessionDetails?>(details);
        }

        public Task<CashSessionCloseStatus> TryCloseAsync(
            int companyId,
            int sessionId,
            int closedByUserId,
            DateOnly closedOn,
            int closingCash,
            CancellationToken cancellationToken)
        {
            if (HasOpenServiceOrders)
            {
                return Task.FromResult(CashSessionCloseStatus.HasOpenServiceOrders);
            }

            var index = Sessions.FindIndex(session =>
                session.CompanyId == companyId && session.Id == sessionId && session.ClosedOn is null);
            if (index < 0)
            {
                return Task.FromResult(CashSessionCloseStatus.NotOpen);
            }

            Sessions[index] = Sessions[index] with
            {
                ClosedOn = closedOn,
                ClosedByUserId = closedByUserId,
                ClosingCash = closingCash,
                Status = CashSessionStatus.Closed.ToString()
            };
            return Task.FromResult(CashSessionCloseStatus.Closed);
        }
    }
}
