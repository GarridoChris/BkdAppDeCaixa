using AppDeCaixa.Domain;
using AppDeCaixa.Domain.CashSessions;

namespace AppDeCaixa.Application.CashSessions;

public sealed record CashSessionDetails(
    int Id,
    int CompanyId,
    DateOnly OpenedOn,
    DateOnly? ClosedOn,
    int OpenedByUserId,
    int? ClosedByUserId,
    int OpeningCash,
    int? ClosingCash,
    string Status);

public interface ICashSessionRepository
{
    Task<CashSessionDetails?> GetCurrentAsync(int companyId, CancellationToken cancellationToken);
    Task<CashSessionDetails?> GetByIdAsync(int companyId, int sessionId, CancellationToken cancellationToken);
    Task<CashSessionDetails?> CreateAsync(CashSession session, CancellationToken cancellationToken);
    Task<CashSessionCloseStatus> TryCloseAsync(
        int companyId,
        int sessionId,
        int closedByUserId,
        DateOnly closedOn,
        int closingCash,
        CancellationToken cancellationToken);
}

public enum CashSessionCloseStatus
{
    Closed,
    NotOpen,
    HasOpenServiceOrders
}

public enum CashSessionOperationStatus
{
    Success,
    ValidationError,
    NotFound,
    Conflict
}

public sealed record CashSessionOperationResult(
    CashSessionOperationStatus Status,
    CashSessionDetails? Session,
    string? Error);

public sealed class CashSessionService(ICashSessionRepository repository)
{
    public Task<CashSessionDetails?> GetCurrentAsync(int companyId, CancellationToken cancellationToken) =>
        repository.GetCurrentAsync(companyId, cancellationToken);

    public Task<CashSessionDetails?> GetByIdAsync(int companyId, int sessionId, CancellationToken cancellationToken) =>
        repository.GetByIdAsync(companyId, sessionId, cancellationToken);

    public async Task<CashSessionOperationResult> OpenAsync(
        int companyId,
        int openedByUserId,
        DateOnly businessDate,
        int openingCash,
        CancellationToken cancellationToken)
    {
        if (openingCash < 0)
        {
            return ValidationError("Opening cash cannot be negative.");
        }

        if (await repository.GetCurrentAsync(companyId, cancellationToken) is not null)
        {
            return Conflict("A cash session is already open for this company.");
        }

        var session = CashSession.Open(companyId, openedByUserId, businessDate, Money.FromWholeReais(openingCash));
        var created = await repository.CreateAsync(session, cancellationToken);
        return created is null
            ? Conflict("A cash session was opened by another request. Refresh and try again.")
            : new CashSessionOperationResult(CashSessionOperationStatus.Success, created, null);
    }

    public async Task<CashSessionOperationResult> CloseAsync(
        int companyId,
        int sessionId,
        int closedByUserId,
        DateOnly businessDate,
        int countedCash,
        CancellationToken cancellationToken)
    {
        if (countedCash < 0)
        {
            return ValidationError("Counted cash cannot be negative.");
        }

        var current = await repository.GetByIdAsync(companyId, sessionId, cancellationToken);
        if (current is null)
        {
            return new CashSessionOperationResult(CashSessionOperationStatus.NotFound, null, "Cash session was not found.");
        }

        if (current.ClosedOn is not null)
        {
            return Conflict("This cash session is already closed.");
        }

        if (businessDate < current.OpenedOn)
        {
            return ValidationError("Closing date cannot be before the opening date.");
        }

        var session = CashSession.Open(
            current.CompanyId,
            current.OpenedByUserId,
            current.OpenedOn,
            Money.FromWholeReais(current.OpeningCash));
        session.Close(closedByUserId, businessDate, Money.FromWholeReais(countedCash));

        var closeStatus = await repository.TryCloseAsync(
            companyId,
            sessionId,
            closedByUserId,
            businessDate,
            countedCash,
            cancellationToken);
        if (closeStatus == CashSessionCloseStatus.HasOpenServiceOrders)
        {
            return Conflict("Close or pay all open service orders before closing the cash session.");
        }

        if (closeStatus != CashSessionCloseStatus.Closed)
        {
            return Conflict("This cash session was closed by another request. Refresh to see its final details.");
        }

        var details = await repository.GetByIdAsync(companyId, sessionId, cancellationToken);
        return new CashSessionOperationResult(CashSessionOperationStatus.Success, details, null);
    }

    private static CashSessionOperationResult ValidationError(string error) =>
        new(CashSessionOperationStatus.ValidationError, null, error);

    private static CashSessionOperationResult Conflict(string error) =>
        new(CashSessionOperationStatus.Conflict, null, error);
}
