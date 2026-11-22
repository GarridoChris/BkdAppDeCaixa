using AppDeCaixa.Application.Commissions;
using AppDeCaixa.Domain;
using AppDeCaixa.Domain.Commissions;
using AppDeCaixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AppDeCaixa.Infrastructure.Commissions;

public sealed class EmployeeCommissionRepository(AppDbContext dbContext) : IEmployeeCommissionRepository
{
    public async Task<EmployeeCommissionResult> GetOwnCommissionsAsync(
        int companyId,
        int collaboratorId,
        CommissionPeriod period,
        CancellationToken cancellationToken)
    {
        var collaborator = await dbContext.Database.SqlQuery<CollaboratorRow>($"""
            SELECT colaborador_id AS "Id", nome AS "Name"
            FROM public.colaborador
            WHERE empresa_id = {companyId} AND colaborador_id = {collaboratorId}
            """).SingleOrDefaultAsync(cancellationToken);

        if (collaborator is null)
        {
            return new EmployeeCommissionResult(CommissionOperationStatus.NotFound, null,
                "The employee profile was not found in this company.");
        }

        var rows = await dbContext.Database.SqlQuery<EmployeeCommissionRow>($"""
            SELECT
                order_record.comanda_id AS "OrderId",
                order_record.cliente_nome AS "ClientName",
                item.comanda_servico_id AS "ServiceOrderItemId",
                COALESCE(item.servicos_id, 0) AS "ServiceId",
                COALESCE(service.nome, service.descricao, 'Service') AS "ServiceName",
                order_record.data_hora_fechamento AS "OrderClosedAt",
                item.valor_unitario AS "ChargedAmount",
                item.porcentagem_colaborador AS "CommissionPercentage",
                (COALESCE(item.pago, FALSE) OR payout_item.item_id IS NOT NULL) AS "IsPaid",
                payout.pagamento_id AS "PayoutId",
                payout.data_pagamento AS "PaymentDate",
                payout.forma_pagamento AS "PaymentMethod",
                payout_item.valor AS "PaidCommissionAmount"
            FROM public.comanda_servico AS item
            INNER JOIN public.comanda AS order_record
                ON order_record.comanda_id = item.comanda_id
            LEFT JOIN public.servicos AS service
                ON service.servicos_id = item.servicos_id
            LEFT JOIN public.pagamento_colaborador_item AS payout_item
                ON payout_item.comanda_servico_id = item.comanda_servico_id
            LEFT JOIN public.pagamento_colaborador AS payout
                ON payout.pagamento_id = payout_item.pagamento_id
            WHERE order_record.empresa_id = {companyId}
                AND item.colaborador_id = {collaboratorId}
                AND order_record.data_hora_fechamento IS NOT NULL
                AND order_record.data_hora_fechamento::date BETWEEN {period.StartDate} AND {period.EndDate}
                AND lower(btrim(order_record.status)) = 'closed'
            ORDER BY order_record.data_hora_fechamento, order_record.comanda_id, item.comanda_servico_id
            """).ToListAsync(cancellationToken);

        var items = rows.Select(row =>
        {
            var amount = row.PaidCommissionAmount ?? CommissionCalculator.Calculate(
                Money.FromWholeReais(row.ChargedAmount), row.CommissionPercentage).WholeReais;

            return new EmployeeCommissionLine(
                row.OrderId,
                row.ClientName,
                row.ServiceOrderItemId,
                row.ServiceId,
                row.ServiceName,
                DateTime.SpecifyKind(row.OrderClosedAt, DateTimeKind.Unspecified),
                row.ChargedAmount,
                row.CommissionPercentage,
                amount,
                row.IsPaid,
                row.PayoutId,
                row.PaymentDate,
                row.PaymentMethod);
        }).ToArray();

        var paidTotal = items.Where(item => item.IsPaid).Sum(item => (long)item.CommissionAmount);
        var total = items.Sum(item => (long)item.CommissionAmount);

        return new EmployeeCommissionResult(
            CommissionOperationStatus.Success,
            new EmployeeCommissionSummary(
                collaborator.Id,
                collaborator.Name,
                CommissionService.Describe(period),
                items.Length,
                total,
                paidTotal,
                total - paidTotal,
                items),
            null);
    }

    private sealed class CollaboratorRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class EmployeeCommissionRow
    {
        public int OrderId { get; set; }
        public string? ClientName { get; set; }
        public int ServiceOrderItemId { get; set; }
        public int ServiceId { get; set; }
        public string ServiceName { get; set; } = "";
        public DateTime OrderClosedAt { get; set; }
        public int ChargedAmount { get; set; }
        public int CommissionPercentage { get; set; }
        public bool IsPaid { get; set; }
        public int? PayoutId { get; set; }
        public DateOnly? PaymentDate { get; set; }
        public string? PaymentMethod { get; set; }
        public int? PaidCommissionAmount { get; set; }
    }
}
