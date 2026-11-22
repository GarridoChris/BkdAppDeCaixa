using AppDeCaixa.Application.Payments;
using AppDeCaixa.Application.ServiceOrders;
using AppDeCaixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace AppDeCaixa.Infrastructure.Payments;

public sealed class DebtSettlementRepository(AppDbContext dbContext) : IDebtSettlementRepository
{
    public async Task<IReadOnlyList<OutstandingDebtDetails>> ListOutstandingAsync(
        int companyId,
        string? clientName,
        int limit,
        CancellationToken cancellationToken)
    {
        var searchName = clientName ?? "";
        var rows = await dbContext.Database.SqlQuery<OutstandingDebtRow>($"""
            SELECT
                order_record.comanda_id AS "OrderId",
                order_record.cliente_nome AS "ClientName",
                order_record.data_hora_fechamento AS "ClosedAt",
                balance."OriginalDebt" AS "OriginalDebt",
                balance."PreviouslySettled" AS "PreviouslySettled",
                balance."OriginalDebt" - balance."PreviouslySettled" AS "OutstandingBalance"
            FROM public.comanda AS order_record
            CROSS JOIN LATERAL (
                SELECT
                    COALESCE(SUM(payment.valor) FILTER (
                        WHERE lower(btrim(method.descricao)) = 'fiado'
                            AND payment.liquidacao_fiado = FALSE), 0)::bigint AS "OriginalDebt",
                    COALESCE(SUM(payment.valor) FILTER (
                        WHERE payment.liquidacao_fiado = TRUE), 0)::bigint AS "PreviouslySettled"
                FROM public.comanda_pagamento AS payment
                INNER JOIN public.forma_pagamento AS method
                    ON method.forma_pagamento_id = payment.forma_pagamento_id
                WHERE payment.comanda_id = order_record.comanda_id
            ) AS balance
            WHERE order_record.empresa_id = {companyId}
                AND order_record.fiado = TRUE
                AND order_record.data_hora_fechamento IS NOT NULL
                AND lower(btrim(order_record.status)) = 'closed'
                AND balance."OriginalDebt" > balance."PreviouslySettled"
                AND (btrim({searchName}) = '' OR lower(btrim(order_record.cliente_nome)) = lower(btrim({searchName})))
            ORDER BY order_record.data_hora_fechamento DESC, order_record.comanda_id DESC
            LIMIT {limit}
            """).ToListAsync(cancellationToken);

        return rows.Select(row => new OutstandingDebtDetails(
            row.OrderId,
            row.ClientName,
            DateTime.SpecifyKind(row.ClosedAt, DateTimeKind.Unspecified),
            row.OriginalDebt,
            row.PreviouslySettled,
            row.OutstandingBalance)).ToArray();
    }

    public async Task<DebtSettlementResult> RecordAsync(
        int companyId,
        int orderId,
        int userId,
        IReadOnlyList<DebtSettlementAllocation> allocations,
        string? note,
        DateTime recordedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var orders = await dbContext.ServiceOrders
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.comanda
                WHERE comanda_id = {orderId} AND empresa_id = {companyId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        var order = orders.SingleOrDefault();
        if (order is null)
        {
            return NotFound("Service order was not found.");
        }

        if (order.ClosedAt is null || !string.Equals(order.Status, "Closed", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict("Only a closed service order can have its Fiado balance settled.");
        }

        if (order.IsDebt != true)
        {
            return Conflict("This service order was not authorized as Fiado.");
        }

        var balance = await dbContext.Database.SqlQuery<DebtBalanceRow>($"""
            SELECT
                COALESCE(SUM(payment.valor) FILTER (
                    WHERE lower(btrim(method.descricao)) = 'fiado'
                        AND COALESCE(payment.liquidacao_fiado, FALSE) = FALSE), 0)::bigint AS "OriginalDebt",
                COALESCE(SUM(payment.valor) FILTER (
                    WHERE COALESCE(payment.liquidacao_fiado, FALSE) = TRUE), 0)::bigint AS "PreviouslySettled",
                COUNT(*) FILTER (
                    WHERE lower(btrim(method.descricao)) = 'fiado'
                        AND COALESCE(payment.liquidacao_fiado, FALSE) = FALSE)::bigint AS "OriginalDebtPaymentCount"
            FROM public.comanda_pagamento AS payment
            INNER JOIN public.forma_pagamento AS method
                ON method.forma_pagamento_id = payment.forma_pagamento_id
            WHERE payment.comanda_id = {orderId}
            """).SingleAsync(cancellationToken);

        if (balance.OriginalDebtPaymentCount != 1 || balance.OriginalDebt <= 0 ||
            order.TotalValue is not > 0 || balance.OriginalDebt != order.TotalValue.Value)
        {
            return Conflict("The order does not have one valid original Fiado payment matching its total.");
        }

        if (balance.PreviouslySettled < 0 || balance.PreviouslySettled >= balance.OriginalDebt)
        {
            return Conflict("This Fiado balance has already been fully settled or its payment history is inconsistent.");
        }

        var settlementTotal = allocations.Sum(allocation => (long)allocation.Amount);
        var outstanding = balance.OriginalDebt - balance.PreviouslySettled;
        if (settlementTotal > outstanding)
        {
            return Conflict($"Settlement amount exceeds the outstanding balance of {outstanding} whole reais.");
        }

        var paymentMethods = new List<PaymentMethodRow>(allocations.Count);
        foreach (var allocation in allocations)
        {
            var method = await dbContext.Database.SqlQuery<PaymentMethodRow>($"""
                SELECT forma_pagamento_id AS "Id", descricao AS "Name"
                FROM public.forma_pagamento
                WHERE forma_pagamento_id = {allocation.PaymentMethodId}
                    AND lower(btrim(descricao)) IN
                        ('pix', 'dinheiro', 'cartão de débito', 'cartão de crédito')
                """).SingleOrDefaultAsync(cancellationToken);

            if (method is null)
            {
                return ValidationError("Debt settlement methods must be Pix, Dinheiro, debit card, or credit card.");
            }

            paymentMethods.Add(method);
        }

        var openSessions = await dbContext.CashSessions
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.caixa
                WHERE empresa_id = {companyId} AND data_fechamento IS NULL
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var openSession = openSessions.SingleOrDefault();
        if (openSession is null)
        {
            return Conflict("Open a cash session before recording a debt settlement.");
        }

        var paymentTime = DateTime.SpecifyKind(recordedAt, DateTimeKind.Unspecified);
        var payments = new List<OrderPaymentDetails>(allocations.Count);
        for (var index = 0; index < allocations.Count; index++)
        {
            var allocation = allocations[index];
            var method = paymentMethods[index];
            var paymentId = await InsertSettlementPaymentAsync(
                orderId,
                openSession.Id,
                allocation.PaymentMethodId,
                allocation.Amount,
                userId,
                note,
                paymentTime,
                transaction.GetDbTransaction(),
                cancellationToken);

            payments.Add(new OrderPaymentDetails
            {
                Id = paymentId,
                OrderId = orderId,
                PaymentMethodId = allocation.PaymentMethodId,
                PaymentMethod = method.Name,
                Amount = allocation.Amount,
                RecordedAt = paymentTime,
                RecordedByUserId = userId,
                Note = note,
                IsDebt = false,
                IsDebtSettlement = true,
                CashSessionId = openSession.Id
            });
        }

        await transaction.CommitAsync(cancellationToken);

        var newOutstanding = outstanding - settlementTotal;
        return new DebtSettlementResult(
            DebtSettlementStatus.Success,
            new DebtSettlementDetails(
                orderId,
                order.ClientName,
                openSession.Id,
                balance.OriginalDebt,
                balance.PreviouslySettled,
                checked((int)settlementTotal),
                newOutstanding,
                newOutstanding == 0,
                payments),
            null);
    }

    private static async Task<int> InsertSettlementPaymentAsync(
        int orderId,
        int cashSessionId,
        int paymentMethodId,
        int amount,
        int userId,
        string? note,
        DateTime recordedAt,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO public.comanda_pagamento
                (comanda_id, forma_pagamento_id, valor, data_pagamento, usuario_id_registro,
                 observacao, caixa_id, liquidacao_fiado)
            VALUES
                (@orderId, @paymentMethodId, @amount, @recordedAt, @userId,
                 @note, @cashSessionId, TRUE)
            RETURNING comanda_pagamento_id
            """;
        command.Parameters.Add(new NpgsqlParameter("orderId", NpgsqlDbType.Integer) { Value = orderId });
        command.Parameters.Add(new NpgsqlParameter("paymentMethodId", NpgsqlDbType.Integer) { Value = paymentMethodId });
        command.Parameters.Add(new NpgsqlParameter("amount", NpgsqlDbType.Integer) { Value = amount });
        command.Parameters.Add(new NpgsqlParameter("recordedAt", NpgsqlDbType.Timestamp) { Value = recordedAt });
        command.Parameters.Add(new NpgsqlParameter("userId", NpgsqlDbType.Integer) { Value = userId });
        command.Parameters.Add(new NpgsqlParameter("note", NpgsqlDbType.Text) { Value = (object?)note ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("cashSessionId", NpgsqlDbType.Integer) { Value = cashSessionId });

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static DebtSettlementResult NotFound(string error) =>
        new(DebtSettlementStatus.NotFound, null, error);

    private static DebtSettlementResult Conflict(string error) =>
        new(DebtSettlementStatus.Conflict, null, error);

    private static DebtSettlementResult ValidationError(string error) =>
        new(DebtSettlementStatus.ValidationError, null, error);

    private sealed class DebtBalanceRow
    {
        public long OriginalDebt { get; set; }
        public long PreviouslySettled { get; set; }
        public long OriginalDebtPaymentCount { get; set; }
    }

    private sealed class OutstandingDebtRow
    {
        public int OrderId { get; set; }
        public string? ClientName { get; set; }
        public DateTime ClosedAt { get; set; }
        public long OriginalDebt { get; set; }
        public long PreviouslySettled { get; set; }
        public long OutstandingBalance { get; set; }
    }

    private sealed class PaymentMethodRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
