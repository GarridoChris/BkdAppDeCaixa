using AppDeCaixa.Application.ServiceOrders;
using AppDeCaixa.Domain;
using AppDeCaixa.Domain.ServiceOrders;
using AppDeCaixa.Infrastructure.Persistence;
using AppDeCaixa.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace AppDeCaixa.Infrastructure.ServiceOrders;

public sealed class ServiceOrderRepository(AppDbContext dbContext) : IServiceOrderRepository
{
    public async Task<ServiceOrderDetails?> GetByIdAsync(
        int companyId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.ServiceOrders
            .AsNoTracking()
            .Where(candidate => candidate.CompanyId == companyId && candidate.Id == orderId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.ClientName,
                CashSessionId = candidate.CashSessionId ?? 0,
                candidate.OpenedAt,
                candidate.ClosedAt,
                Status = candidate.Status ?? "Open",
                TotalValue = candidate.TotalValue ?? 0,
                candidate.IsDebt,
                OpenedByUserId = candidate.OpenedByUserId ?? 0
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return null;
        }

        var items = await dbContext.Database.SqlQuery<ServiceOrderItemDetails>($"""
            SELECT
                item.comanda_servico_id AS "ItemId",
                COALESCE(item.servicos_id, 0) AS "ServiceId",
                COALESCE(service.nome, '') AS "ServiceName",
                COALESCE(service.descricao, '') AS "ServiceDescription",
                COALESCE(item.colaborador_id, 0) AS "CollaboratorId",
                COALESCE(collaborator.nome, '') AS "CollaboratorName",
                item.valor_tabela AS "CatalogPrice",
                item.valor_unitario AS "UnitPrice",
                item.porcentagem_colaborador AS "CommissionPercentage",
                item.adicional AS "IsAdditional",
                item.observacao AS "Observation",
                item.pago AS "IsPaid",
                item.motivo_ajuste AS "AdjustmentReason",
                item.usuario_id_ajuste AS "AdjustedByUserId",
                item.data_hora_ajuste AS "AdjustedAt"
            FROM public.comanda_servico AS item
            LEFT JOIN public.servicos AS service
                ON service.servicos_id = item.servicos_id
                AND service.empresa_id = {companyId}
            LEFT JOIN public.colaborador AS collaborator
                ON collaborator.colaborador_id = item.colaborador_id
                AND collaborator.empresa_id = {companyId}
            WHERE item.comanda_id = {orderId}
            ORDER BY item.comanda_servico_id
            """).ToListAsync(cancellationToken);

        var payments = await dbContext.Database.SqlQuery<OrderPaymentDetails>($"""
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
            ORDER BY payment.comanda_pagamento_id
            """).ToListAsync(cancellationToken);

        var originalFiado = payments
            .Where(payment => payment.IsDebt && !payment.IsDebtSettlement)
            .Sum(payment => (long)payment.Amount);
        var settledFiado = payments
            .Where(payment => payment.IsDebtSettlement)
            .Sum(payment => (long)payment.Amount);
        var outstandingDebt = order.IsDebt == true
            ? Math.Max(0, originalFiado - settledFiado)
            : 0;

        return new ServiceOrderDetails(
            order.Id,
            order.ClientName,
            order.CashSessionId,
            order.OpenedAt,
            order.ClosedAt,
            order.Status,
            order.TotalValue,
            order.IsDebt,
            outstandingDebt,
            order.OpenedByUserId,
            items,
            payments);
    }

    public async Task<ServiceOrderOperationResult> OpenAsync(
        int companyId,
        int openedByUserId,
        string clientName,
        DateTime openedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
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
            return Conflict("Open a cash session before creating a service order.");
        }

        var order = new ServiceOrderRecord
        {
            CompanyId = companyId,
            ClientName = clientName,
            CashSessionId = openSession.Id,
            OpenedAt = DateTime.SpecifyKind(openedAt, DateTimeKind.Unspecified),
            OpenedByUserId = openedByUserId,
            Status = "Open",
            TotalValue = 0,
            IsDebt = false
        };

        dbContext.ServiceOrders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var details = await GetByIdAsync(companyId, order.Id, cancellationToken);
        return Success(details);
    }

    public async Task<ServiceOrderOperationResult> AddServiceAsync(
        int companyId,
        int orderId,
        int serviceId,
        int collaboratorId,
        string? observation,
        int adjustedByUserId,
        int? priceOverride,
        string? adjustmentReason,
        DateTime adjustedAt,
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

        if (order.ClosedAt is not null || !string.Equals(order.Status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict("Only an open service order can be changed.");
        }

        if (order.CashSessionId is null || !await IsCashSessionOpenAsync(
                companyId, order.CashSessionId.Value, cancellationToken))
        {
            return Conflict("The cash session linked to this order is no longer open.");
        }

        if (order.OpenedByUserId is not > 0 || order.TotalValue is < 0)
        {
            return Conflict("The order is missing valid opening details or has an invalid total.");
        }

        var service = await dbContext.Services
            .AsNoTracking()
            .Where(candidate => candidate.Id == serviceId && candidate.CompanyId == companyId)
            .Select(candidate => new
            {
                candidate.Price,
                candidate.CommissionPercentage,
                candidate.IsActive
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (service?.IsActive != true)
        {
            return NotFound("Active service was not found for this company.");
        }

        if (priceOverride == service.Price)
        {
            return ValidationError("Price override must differ from the service's catalog price.");
        }

        var collaboratorIsActive = await dbContext.Set<CollaboratorRecord>().AnyAsync(
            candidate => candidate.Id == collaboratorId &&
                candidate.CompanyId == companyId &&
                candidate.IsActive == true,
            cancellationToken);
        if (!collaboratorIsActive)
        {
            return NotFound("Active collaborator was not found for this company.");
        }

        var unitPrice = priceOverride ?? service.Price;
        var orderDomain = ServiceOrder.RestoreOpen(
            companyId,
            order.CashSessionId.Value,
            order.OpenedByUserId.Value,
            Money.FromWholeReais(order.TotalValue ?? 0));

        Money newTotal;
        try
        {
            newTotal = orderDomain.AddService(
                Money.FromWholeReais(unitPrice),
                service.CommissionPercentage);
        }
        catch (ArgumentOutOfRangeException)
        {
            return ValidationError("The selected service has an invalid price or commission percentage.");
        }
        catch (OverflowException)
        {
            return ValidationError("The service would exceed the maximum order total supported by the database.");
        }

        int? adjustmentActorId = priceOverride is null ? null : adjustedByUserId;
        DateTime? adjustmentDate = priceOverride is null
            ? null
            : DateTime.SpecifyKind(adjustedAt, DateTimeKind.Unspecified);
        var isAdditional = priceOverride is not null && unitPrice > service.Price;
        var adjustmentDateParameter = new NpgsqlParameter("adjustmentDate", NpgsqlDbType.Timestamp)
        {
            Value = (object?)adjustmentDate ?? DBNull.Value
        };

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.comanda_servico
                (comanda_id, servicos_id, colaborador_id, valor_unitario,
                 porcentagem_colaborador, adicional, observacao, pago, valor_tabela,
                 motivo_ajuste, usuario_id_ajuste, data_hora_ajuste)
            VALUES
                ({orderId}, {serviceId}, {collaboratorId}, {unitPrice},
                 {service.CommissionPercentage}, {isAdditional}, {observation}, FALSE,
                 {service.Price}, {adjustmentReason}, {adjustmentActorId}, {adjustmentDateParameter})
            """, cancellationToken);

        order.TotalValue = newTotal.WholeReais;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var details = await GetByIdAsync(companyId, orderId, cancellationToken);
        return Success(details);
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

    private static ServiceOrderOperationResult Success(ServiceOrderDetails? order) =>
        new(ServiceOrderOperationStatus.Success, order, null);

    private static ServiceOrderOperationResult ValidationError(string error) =>
        new(ServiceOrderOperationStatus.ValidationError, null, error);

    private static ServiceOrderOperationResult NotFound(string error) =>
        new(ServiceOrderOperationStatus.NotFound, null, error);

    private static ServiceOrderOperationResult Conflict(string error) =>
        new(ServiceOrderOperationStatus.Conflict, null, error);

}
