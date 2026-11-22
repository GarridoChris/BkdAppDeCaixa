using AppDeCaixa.Application.CashSessions;
using AppDeCaixa.Domain.CashSessions;
using AppDeCaixa.Infrastructure.Persistence;
using AppDeCaixa.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AppDeCaixa.Infrastructure.CashSessions;

public sealed class CashMovementRepository(AppDbContext dbContext) : ICashMovementRepository
{
    public async Task<CashMovementOperationResult> CreateAsync(
        int companyId,
        CashMovement movement,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var sessions = await dbContext.CashSessions
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.caixa
                WHERE caixa_id = {movement.CashSessionId}
                    AND empresa_id = {companyId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        var session = sessions.SingleOrDefault();
        if (session is null)
        {
            return NotFound("Cash session was not found.");
        }

        if (session.ClosedOn is not null)
        {
            return Conflict("Cash movements can only be recorded in an open cash session.");
        }

        if (movement.Direction == CashMovementDirection.CashOut)
        {
            var reconciliation = await ReadReconciliationAsync(companyId, movement.CashSessionId, cancellationToken);
            if (reconciliation is null)
            {
                return NotFound("Cash session was not found.");
            }

            if (movement.Amount.WholeReais > reconciliation.ExpectedCash)
            {
                return Conflict("Cash withdrawal exceeds the expected cash currently available in the drawer.");
            }
        }

        var record = new CashMovementRecord
        {
            CashSessionId = movement.CashSessionId,
            Direction = movement.Direction.ToString(),
            Amount = movement.Amount.WholeReais,
            Note = movement.Note,
            PerformedByUserId = movement.PerformedByUserId,
            RecordedAt = movement.RecordedAt
        };
        dbContext.CashMovements.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Success(ToDetails(record));
    }

    public async Task<IReadOnlyList<CashMovementDetails>?> ListAsync(
        int companyId,
        int cashSessionId,
        CancellationToken cancellationToken)
    {
        var sessionExists = await dbContext.CashSessions.AnyAsync(
            session => session.CompanyId == companyId && session.Id == cashSessionId,
            cancellationToken);
        if (!sessionExists)
        {
            return null;
        }

        return await (
            from movement in dbContext.CashMovements.AsNoTracking()
            join session in dbContext.CashSessions.AsNoTracking()
                on movement.CashSessionId equals session.Id
            where session.CompanyId == companyId && session.Id == cashSessionId
            orderby movement.RecordedAt, movement.Id
            select new CashMovementDetails(
                movement.Id,
                movement.CashSessionId,
                movement.Direction,
                movement.Amount,
                movement.Note,
                movement.PerformedByUserId,
                movement.RecordedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<CashSessionReconciliation?> GetReconciliationAsync(
        int companyId,
        int cashSessionId,
        CancellationToken cancellationToken) =>
        ReadReconciliationAsync(companyId, cashSessionId, cancellationToken);

    private async Task<CashSessionReconciliation?> ReadReconciliationAsync(
        int companyId,
        int cashSessionId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Database.SqlQuery<ReconciliationRow>($"""
            WITH payment_totals AS (
                SELECT
                    COALESCE(payment.caixa_id, order_record.caixa_id) AS "CashSessionId",
                    COALESCE(SUM(payment.valor) FILTER (WHERE lower(btrim(method.descricao)) = 'dinheiro'), 0)::bigint AS "CashSales",
                    COALESCE(SUM(payment.valor) FILTER (WHERE lower(btrim(method.descricao)) = 'pix'), 0)::bigint AS "PixSales",
                    COALESCE(SUM(payment.valor) FILTER (WHERE lower(btrim(method.descricao)) = 'cartão de débito'), 0)::bigint AS "DebitCardSales",
                    COALESCE(SUM(payment.valor) FILTER (WHERE lower(btrim(method.descricao)) = 'cartão de crédito'), 0)::bigint AS "CreditCardSales",
                    COALESCE(SUM(payment.valor) FILTER (WHERE lower(btrim(method.descricao)) = 'fiado'), 0)::bigint AS "FiadoDebt",
                    COALESCE(SUM(payment.valor) FILTER (WHERE COALESCE(payment.liquidacao_fiado, FALSE)), 0)::bigint AS "FiadoSettlements"
                FROM public.comanda_pagamento AS payment
                JOIN public.comanda AS order_record
                    ON order_record.comanda_id = payment.comanda_id
                JOIN public.forma_pagamento AS method
                    ON method.forma_pagamento_id = payment.forma_pagamento_id
                WHERE order_record.empresa_id = {companyId}
                    AND COALESCE(payment.caixa_id, order_record.caixa_id) = {cashSessionId}
                GROUP BY COALESCE(payment.caixa_id, order_record.caixa_id)
            ), movement_totals AS (
                SELECT
                    caixa_id AS "CashSessionId",
                    COALESCE(SUM(valor) FILTER (WHERE tipo = 'CashIn'), 0)::bigint AS "CashIn",
                    COALESCE(SUM(valor) FILTER (WHERE tipo = 'CashOut'), 0)::bigint AS "CashOut"
                FROM public.caixa_movimento
                WHERE caixa_id = {cashSessionId}
                GROUP BY caixa_id
            )
            SELECT
                session.caixa_id AS "CashSessionId",
                COALESCE(session.status, CASE WHEN session.data_fechamento IS NULL THEN 'Open' ELSE 'Closed' END) AS "Status",
                session.valor_abertura AS "OpeningCash",
                COALESCE(payment_totals."CashSales", 0)::bigint AS "CashSales",
                COALESCE(movement_totals."CashIn", 0)::bigint AS "CashIn",
                COALESCE(movement_totals."CashOut", 0)::bigint AS "CashOut",
                COALESCE(payment_totals."PixSales", 0)::bigint AS "PixSales",
                COALESCE(payment_totals."DebitCardSales", 0)::bigint AS "DebitCardSales",
                COALESCE(payment_totals."CreditCardSales", 0)::bigint AS "CreditCardSales",
                COALESCE(payment_totals."FiadoDebt", 0)::bigint AS "FiadoDebt",
                COALESCE(payment_totals."FiadoSettlements", 0)::bigint AS "FiadoSettlements",
                (
                    session.valor_abertura::bigint
                    + COALESCE(payment_totals."CashSales", 0)::bigint
                    + COALESCE(movement_totals."CashIn", 0)::bigint
                    - COALESCE(movement_totals."CashOut", 0)::bigint
                ) AS "ExpectedCash",
                session.valor_fechamento AS "CountedCash",
                CASE WHEN session.valor_fechamento IS NULL THEN NULL ELSE
                    session.valor_fechamento::bigint - (
                        session.valor_abertura::bigint
                        + COALESCE(payment_totals."CashSales", 0)::bigint
                        + COALESCE(movement_totals."CashIn", 0)::bigint
                        - COALESCE(movement_totals."CashOut", 0)::bigint
                    )
                END AS "Difference"
            FROM public.caixa AS session
            LEFT JOIN payment_totals ON payment_totals."CashSessionId" = session.caixa_id
            LEFT JOIN movement_totals ON movement_totals."CashSessionId" = session.caixa_id
            WHERE session.empresa_id = {companyId} AND session.caixa_id = {cashSessionId}
            """).SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new CashSessionReconciliation(
                row.CashSessionId,
                row.Status,
                row.OpeningCash,
                row.CashSales,
                row.CashIn,
                row.CashOut,
                row.PixSales,
                row.DebitCardSales,
                row.CreditCardSales,
                row.FiadoDebt,
                row.FiadoSettlements,
                row.ExpectedCash,
                row.CountedCash,
                row.Difference);
    }

    private static CashMovementDetails ToDetails(CashMovementRecord record) => new(
        record.Id,
        record.CashSessionId,
        record.Direction,
        record.Amount,
        record.Note,
        record.PerformedByUserId,
        record.RecordedAt);

    private static CashMovementOperationResult Success(CashMovementDetails movement) =>
        new(CashMovementOperationStatus.Success, movement, null);

    private static CashMovementOperationResult NotFound(string error) =>
        new(CashMovementOperationStatus.NotFound, null, error);

    private static CashMovementOperationResult Conflict(string error) =>
        new(CashMovementOperationStatus.Conflict, null, error);

    private sealed class ReconciliationRow
    {
        public int CashSessionId { get; set; }
        public string Status { get; set; } = "";
        public int OpeningCash { get; set; }
        public long CashSales { get; set; }
        public long CashIn { get; set; }
        public long CashOut { get; set; }
        public long PixSales { get; set; }
        public long DebitCardSales { get; set; }
        public long CreditCardSales { get; set; }
        public long FiadoDebt { get; set; }
        public long FiadoSettlements { get; set; }
        public long ExpectedCash { get; set; }
        public int? CountedCash { get; set; }
        public long? Difference { get; set; }
    }
}
