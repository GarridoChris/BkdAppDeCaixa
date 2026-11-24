using AppDeCaixa.Application.Payments;
using AppDeCaixa.Application.ServiceOrders;
using AppDeCaixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace AppDeCaixa.Infrastructure.Payments;

public sealed class OrderPaymentRepository(AppDbContext dbContext) : IOrderPaymentRepository
{
    public async Task<IReadOnlyList<PaymentMethodOption>> ListPaymentMethodsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database.SqlQuery<PaymentMethodRow>($"""
            SELECT
                forma_pagamento_id AS "Id",
                descricao AS "Name",
                lower(btrim(descricao)) = 'fiado' AS "RequiresManagerAuthorization"
            FROM public.forma_pagamento
            WHERE lower(btrim(descricao)) IN
                ('pix', 'dinheiro', 'cartão de débito', 'cartão de crédito', 'fiado')
            ORDER BY CASE lower(btrim(descricao))
                WHEN 'pix' THEN 1
                WHEN 'dinheiro' THEN 2
                WHEN 'cartão de débito' THEN 3
                WHEN 'cartão de crédito' THEN 4
                WHEN 'fiado' THEN 5
                ELSE 6
            END, forma_pagamento_id
            """).ToListAsync(cancellationToken);

        return rows.Select(row => new PaymentMethodOption(
            row.Id,
            row.Name,
            row.RequiresManagerAuthorization)).ToArray();
    }

    public async Task<PaymentMethodOption?> GetPaymentMethodAsync(
        int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.Database.SqlQuery<PaymentMethodRow>($"""
            SELECT
                forma_pagamento_id AS "Id",
                descricao AS "Name",
                lower(btrim(descricao)) = 'fiado' AS "RequiresManagerAuthorization"
            FROM public.forma_pagamento
            WHERE forma_pagamento_id = {paymentMethodId}
                AND lower(btrim(descricao)) IN
                    ('pix', 'dinheiro', 'cartão de débito', 'cartão de crédito', 'fiado')
            """).SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new PaymentMethodOption(row.Id, row.Name, row.RequiresManagerAuthorization);
    }

    public async Task<OrderPaymentResult> RecordFullPaymentAsync(
        int companyId,
        int orderId,
        int userId,
        int paymentMethodId,
        bool isManager,
        string? note,
        DateTime recordedAt,
        CancellationToken cancellationToken,
        ExpectedOrder? expectedOrder)
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

        if (order.ClosedAt is not null || !string.Equals(order.Status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict("Only an open service order can be paid.");
        }

        if (order.CashSessionId is not > 0 || !await IsCashSessionOpenAsync(
                companyId,
                order.CashSessionId.Value,
                cancellationToken))
        {
            return Conflict("The order must belong to an open cash session.");
        }

        var method = await GetPaymentMethodAsync(paymentMethodId, cancellationToken);
        if (method is null)
        {
            return ValidationError("Choose a supported payment method.");
        }

        if (method.RequiresManagerAuthorization && !isManager)
        {
            return Forbidden("Only a Manager can authorize Fiado.");
        }

        if (method.RequiresManagerAuthorization && string.IsNullOrWhiteSpace(note))
        {
            return ValidationError("A note is required when authorizing Fiado.");
        }

        var existingPayments = await dbContext.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::integer AS "Value"
            FROM public.comanda_pagamento
            WHERE comanda_id = {orderId}
            """).SingleAsync(cancellationToken);
        if (existingPayments != 0)
        {
            return Conflict("This order already has a payment record.");
        }

        var totals = await dbContext.Database.SqlQuery<PaymentTotals>($"""
            SELECT COALESCE(SUM(valor_unitario), 0) AS "TotalValue", COUNT(*) AS "ItemCount"
            FROM public.comanda_servico
            WHERE comanda_id = {orderId}
            """).SingleAsync(cancellationToken);
        var orderTotal = totals.TotalValue;
        if (expectedOrder is not null && !expectedOrder.Matches(orderTotal, totals.ItemCount))
        {
            return Conflict("The order changed. Review the items and total before confirming payment.");
        }

        if (orderTotal < 0 || orderTotal > int.MaxValue)
        {
            return ValidationError("The order total is outside the amount range supported by the database.");
        }

        var totalValue = checked((int)orderTotal);
        order.TotalValue = totalValue;
        order.ClosedAt = DateTime.SpecifyKind(recordedAt, DateTimeKind.Unspecified);
        order.ClosedByUserId = userId;
        order.Status = "Closed";
        order.IsDebt = method.RequiresManagerAuthorization;

        var recordedAtParameter = new NpgsqlParameter("recordedAt", NpgsqlDbType.Timestamp)
        {
            Value = DateTime.SpecifyKind(recordedAt, DateTimeKind.Unspecified)
        };
        var noteParameter = new NpgsqlParameter("note", NpgsqlDbType.Text)
        {
            Value = (object?)note ?? DBNull.Value
        };

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.comanda_pagamento
                (comanda_id, forma_pagamento_id, valor, data_pagamento, usuario_id_registro, observacao,
                 caixa_id, liquidacao_fiado)
            VALUES
                ({orderId}, {paymentMethodId}, {totalValue},
                 {recordedAtParameter}, {userId}, {noteParameter}, {order.CashSessionId}, FALSE)
            """, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        var payment = await dbContext.Database.SqlQuery<OrderPaymentDetails>($"""
            SELECT
                payment.comanda_pagamento_id AS "Id",
                COALESCE(payment.comanda_id, 0) AS "OrderId",
                COALESCE(payment.forma_pagamento_id, 0) AS "PaymentMethodId",
                COALESCE(method.descricao, '') AS "PaymentMethod",
                payment.valor AS "Amount",
                payment.data_pagamento AS "RecordedAt",
                payment.usuario_id_registro AS "RecordedByUserId",
                payment.observacao AS "Note",
                COALESCE(lower(btrim(method.descricao)) = 'fiado', FALSE) AS "IsDebt",
                COALESCE(payment.liquidacao_fiado, FALSE) AS "IsDebtSettlement",
                payment.caixa_id AS "CashSessionId"
            FROM public.comanda_pagamento AS payment
            LEFT JOIN public.forma_pagamento AS method
                ON method.forma_pagamento_id = payment.forma_pagamento_id
            WHERE payment.comanda_id = {orderId}
            """).SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Success(payment);
    }

    private async Task<bool> IsCashSessionOpenAsync(
        int companyId,
        int cashSessionId,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.CashSessions
            .FromSqlInterpolated($"""
                SELECT *
                FROM public.caixa
                WHERE caixa_id = {cashSessionId}
                    AND empresa_id = {companyId}
                    AND data_fechamento IS NULL
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return sessions.Count == 1;
    }

    private static OrderPaymentResult Success(OrderPaymentDetails payment) =>
        new(OrderPaymentStatus.Success, payment, null);

    private static OrderPaymentResult ValidationError(string error) =>
        new(OrderPaymentStatus.ValidationError, null, error);

    private static OrderPaymentResult Forbidden(string error) =>
        new(OrderPaymentStatus.Forbidden, null, error);

    private static OrderPaymentResult NotFound(string error) =>
        new(OrderPaymentStatus.NotFound, null, error);

    private static OrderPaymentResult Conflict(string error) =>
        new(OrderPaymentStatus.Conflict, null, error);

    private sealed class PaymentMethodRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool RequiresManagerAuthorization { get; set; }
    }

    private sealed class PaymentTotals
    {
        public long TotalValue { get; set; }
        public long ItemCount { get; set; }
    }
}
