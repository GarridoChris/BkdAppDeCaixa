using AppDeCaixa.Application.CashSessions;
using AppDeCaixa.Domain.CashSessions;
using AppDeCaixa.Infrastructure.Persistence;
using AppDeCaixa.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace AppDeCaixa.Infrastructure.CashSessions;

public sealed class CashSessionRepository(AppDbContext dbContext) : ICashSessionRepository
{
    private const string OpenSessionIndex = "caixa_one_open_per_company_idx";

    public Task<CashSessionDetails?> GetCurrentAsync(int companyId, CancellationToken cancellationToken) =>
        dbContext.CashSessions
            .AsNoTracking()
            .Where(session => session.CompanyId == companyId && session.ClosedOn == null)
            .Select(session => new CashSessionDetails(
                session.Id,
                session.CompanyId ?? 0,
                session.OpenedOn,
                session.ClosedOn,
                session.OpenedByUserId ?? 0,
                session.ClosedByUserId,
                session.OpeningCash,
                session.ClosingCash,
                session.Status ?? "Open"))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<CashSessionDetails?> GetByIdAsync(
        int companyId,
        int sessionId,
        CancellationToken cancellationToken) =>
        dbContext.CashSessions
            .AsNoTracking()
            .Where(session => session.CompanyId == companyId && session.Id == sessionId)
            .Select(session => new CashSessionDetails(
                session.Id,
                session.CompanyId ?? 0,
                session.OpenedOn,
                session.ClosedOn,
                session.OpenedByUserId ?? 0,
                session.ClosedByUserId,
                session.OpeningCash,
                session.ClosingCash,
                session.Status ?? (session.ClosedOn == null ? "Open" : "Closed")))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CashSessionDetails?> CreateAsync(CashSession session, CancellationToken cancellationToken)
    {
        var record = new CashSessionRecord
        {
            CompanyId = session.CompanyId,
            OpenedOn = session.BusinessDate,
            OpenedByUserId = session.OpenedByUserId,
            OpeningCash = session.OpeningCash.WholeReais,
            Status = session.Status.ToString()
        };

        dbContext.CashSessions.Add(record);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsOpenSessionConflict(exception))
        {
            dbContext.Entry(record).State = EntityState.Detached;
            return null;
        }

        return ToDetails(record);
    }

    public async Task<CashSessionCloseStatus> TryCloseAsync(
        int companyId,
        int sessionId,
        int closedByUserId,
        DateOnly closedOn,
        int closingCash,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var sessions = await dbContext.CashSessions
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.caixa
                WHERE caixa_id = {sessionId}
                    AND empresa_id = {companyId}
                    AND data_fechamento IS NULL
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (sessions.Count != 1)
        {
            return CashSessionCloseStatus.NotOpen;
        }

        var hasOpenOrders = await dbContext.OpenOrders(companyId).AnyAsync(
            order => order.CashSessionId == sessionId,
            cancellationToken);
        if (hasOpenOrders)
        {
            return CashSessionCloseStatus.HasOpenServiceOrders;
        }

        var updated = await dbContext.CashSessions
            .Where(session => session.CompanyId == companyId &&
                session.Id == sessionId &&
                session.ClosedOn == null)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(session => session.ClosedOn, closedOn)
                .SetProperty(session => session.ClosedByUserId, (int?)closedByUserId)
                .SetProperty(session => session.ClosingCash, (int?)closingCash)
                .SetProperty(session => session.Status, "Closed"),
                cancellationToken);

        if (updated != 1)
        {
            return CashSessionCloseStatus.NotOpen;
        }

        await transaction.CommitAsync(cancellationToken);
        return CashSessionCloseStatus.Closed;
    }

    private static CashSessionDetails ToDetails(CashSessionRecord record) => new(
        record.Id,
        record.CompanyId ?? 0,
        record.OpenedOn,
        record.ClosedOn,
        record.OpenedByUserId ?? 0,
        record.ClosedByUserId,
        record.OpeningCash,
        record.ClosingCash,
        record.Status ?? (record.ClosedOn == null ? "Open" : "Closed"));

    private static bool IsOpenSessionConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OpenSessionIndex
        };
}
