using AppDeCaixa.Application.Commissions;
using AppDeCaixa.Domain;
using AppDeCaixa.Domain.Commissions;
using AppDeCaixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace AppDeCaixa.Infrastructure.Commissions;

public sealed class CommissionRepository(AppDbContext dbContext) : ICommissionRepository
{
    public async Task<CommissionPreviewResult> GetEligibleAsync(
        int companyId,
        int collaboratorId,
        CommissionPeriod period,
        CancellationToken cancellationToken)
    {
        var collaborator = await GetCollaboratorAsync(companyId, collaboratorId, cancellationToken);
        if (collaborator is null)
        {
            return new CommissionPreviewResult(CommissionOperationStatus.NotFound, null,
                "Collaborator was not found in this company.");
        }

        var lines = await GetEligibleLinesAsync(companyId, collaboratorId, period, cancellationToken);
        return new CommissionPreviewResult(
            CommissionOperationStatus.Success,
            CreatePreview(collaborator, period, lines),
            null);
    }

    public async Task<CommissionPayoutResult> CreatePayoutAsync(
        int companyId,
        int collaboratorId,
        int userId,
        CommissionPeriod period,
        string paymentMethod,
        string? note,
        DateOnly paymentDate,
        DateTime recordedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var collaborator = await GetCollaboratorAsync(companyId, collaboratorId, cancellationToken);
        if (collaborator is null)
        {
            return new CommissionPayoutResult(CommissionOperationStatus.NotFound, null,
                "Collaborator was not found in this company.");
        }

        // Lock the closed orders before reading unpaid items. Concurrent payouts for the same
        // period then wait and see the first payout's committed `pago` updates.
        await dbContext.ServiceOrders
            .FromSqlInterpolated($"""
                SELECT order_record.*
                FROM public.comanda AS order_record
                WHERE order_record.empresa_id = {companyId}
                    AND order_record.data_hora_fechamento IS NOT NULL
                    AND order_record.data_hora_fechamento::date BETWEEN {period.StartDate} AND {period.EndDate}
                    AND lower(btrim(order_record.status)) = 'closed'
                    AND EXISTS (
                        SELECT 1
                        FROM public.comanda_servico AS item
                        WHERE item.comanda_id = order_record.comanda_id
                            AND item.colaborador_id = {collaboratorId}
                    )
                FOR UPDATE OF order_record
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var items = await GetEligibleLinesAsync(companyId, collaboratorId, period, cancellationToken);
        if (items.Count == 0)
        {
            return new CommissionPayoutResult(CommissionOperationStatus.Conflict, null,
                "There are no unpaid commissions for this collaborator and period.");
        }

        var preview = CreatePreview(collaborator, period, items);
        var payoutId = await InsertPayoutHeaderAsync(
            collaboratorId,
            userId,
            paymentDate,
            preview.TotalAmount,
            paymentMethod,
            note,
            recordedAt,
            transaction.GetDbTransaction(),
            cancellationToken);

        foreach (var item in items)
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.pagamento_colaborador_item
                    (pagamento_id, comanda_servico_id, valor)
                VALUES
                    ({payoutId}, {item.ServiceOrderItemId}, {item.CommissionAmount})
                """, cancellationToken);

            var updated = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public.comanda_servico
                SET pago = TRUE
                WHERE comanda_servico_id = {item.ServiceOrderItemId}
                    AND COALESCE(pago, FALSE) = FALSE
                """, cancellationToken);

            if (updated != 1)
            {
                return new CommissionPayoutResult(CommissionOperationStatus.Conflict, null,
                    "An order item changed while its commission payout was being recorded.");
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return new CommissionPayoutResult(
            CommissionOperationStatus.Success,
            new CommissionPayoutDetails(
                payoutId,
                collaborator.Id,
                collaborator.Name,
                CommissionService.Describe(period),
                paymentDate,
                preview.TotalAmount,
                paymentMethod,
                note,
                userId,
                DateTime.SpecifyKind(recordedAt, DateTimeKind.Unspecified),
                preview.Items),
            null);
    }

    private async Task<CollaboratorRow?> GetCollaboratorAsync(
        int companyId,
        int collaboratorId,
        CancellationToken cancellationToken) =>
        await dbContext.Database.SqlQuery<CollaboratorRow>($"""
            SELECT colaborador_id AS "Id", nome AS "Name"
            FROM public.colaborador
            WHERE empresa_id = {companyId} AND colaborador_id = {collaboratorId}
            """).SingleOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyList<CommissionLineDetails>> GetEligibleLinesAsync(
        int companyId,
        int collaboratorId,
        CommissionPeriod period,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Database.SqlQuery<CommissionLineRow>($"""
            SELECT
                item.comanda_servico_id AS "ServiceOrderItemId",
                order_record.comanda_id AS "OrderId",
                order_record.cliente_nome AS "ClientName",
                COALESCE(item.servicos_id, 0) AS "ServiceId",
                COALESCE(service.nome, service.descricao, 'Service') AS "ServiceName",
                order_record.data_hora_fechamento AS "OrderClosedAt",
                item.valor_unitario AS "ChargedAmount",
                item.porcentagem_colaborador AS "CommissionPercentage"
            FROM public.comanda_servico AS item
            INNER JOIN public.comanda AS order_record
                ON order_record.comanda_id = item.comanda_id
            LEFT JOIN public.servicos AS service
                ON service.servicos_id = item.servicos_id
            WHERE order_record.empresa_id = {companyId}
                AND item.colaborador_id = {collaboratorId}
                AND order_record.data_hora_fechamento IS NOT NULL
                AND order_record.data_hora_fechamento::date BETWEEN {period.StartDate} AND {period.EndDate}
                AND lower(btrim(order_record.status)) = 'closed'
                AND COALESCE(item.pago, FALSE) = FALSE
                AND NOT EXISTS (
                    SELECT 1
                    FROM public.pagamento_colaborador_item AS payout_item
                    WHERE payout_item.comanda_servico_id = item.comanda_servico_id
                )
            ORDER BY order_record.data_hora_fechamento, order_record.comanda_id, item.comanda_servico_id
            """).ToListAsync(cancellationToken);

        return rows.Select(row => new CommissionLineDetails(
            row.ServiceOrderItemId,
            row.OrderId,
            row.ClientName,
            row.ServiceId,
            row.ServiceName,
            DateTime.SpecifyKind(row.OrderClosedAt, DateTimeKind.Unspecified),
            row.ChargedAmount,
            row.CommissionPercentage,
            CommissionCalculator.Calculate(Money.FromWholeReais(row.ChargedAmount), row.CommissionPercentage)
                .WholeReais)).ToArray();
    }

    private static CommissionPreview CreatePreview(
        CollaboratorRow collaborator,
        CommissionPeriod period,
        IReadOnlyList<CommissionLineDetails> items)
    {
        var total = items.Aggregate(0L, (sum, item) => checked(sum + item.CommissionAmount));
        if (total > int.MaxValue)
        {
            throw new InvalidOperationException("Commission payout exceeds the amount range supported by the database.");
        }

        return new CommissionPreview(
            collaborator.Id,
            collaborator.Name,
            CommissionService.Describe(period),
            items.Count,
            (int)total,
            items);
    }

    private static async Task<int> InsertPayoutHeaderAsync(
        int collaboratorId,
        int userId,
        DateOnly paymentDate,
        int totalAmount,
        string paymentMethod,
        string? note,
        DateTime recordedAt,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = transaction.Connection!.CreateCommand();
        await using (command)
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO public.pagamento_colaborador
                    (colaborador_id, data_pagamento, valor_total, forma_pagamento, observacao,
                     usuario_id_registro, data_hora_registro)
                VALUES
                    (@collaboratorId, @paymentDate, @totalAmount, @paymentMethod, @note,
                     @userId, @recordedAt)
                RETURNING pagamento_id
                """;
            command.Parameters.Add(new NpgsqlParameter("collaboratorId", NpgsqlDbType.Integer) { Value = collaboratorId });
            command.Parameters.Add(new NpgsqlParameter("paymentDate", NpgsqlDbType.Date) { Value = paymentDate });
            command.Parameters.Add(new NpgsqlParameter("totalAmount", NpgsqlDbType.Integer) { Value = totalAmount });
            command.Parameters.Add(new NpgsqlParameter("paymentMethod", NpgsqlDbType.Varchar) { Value = paymentMethod });
            command.Parameters.Add(new NpgsqlParameter("note", NpgsqlDbType.Text) { Value = (object?)note ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("userId", NpgsqlDbType.Integer) { Value = userId });
            command.Parameters.Add(new NpgsqlParameter("recordedAt", NpgsqlDbType.Timestamp)
            {
                Value = DateTime.SpecifyKind(recordedAt, DateTimeKind.Unspecified)
            });

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }
    }

    private sealed class CollaboratorRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class CommissionLineRow
    {
        public int ServiceOrderItemId { get; set; }
        public int OrderId { get; set; }
        public string? ClientName { get; set; }
        public int ServiceId { get; set; }
        public string ServiceName { get; set; } = "";
        public DateTime OrderClosedAt { get; set; }
        public int ChargedAmount { get; set; }
        public int CommissionPercentage { get; set; }
    }
}
