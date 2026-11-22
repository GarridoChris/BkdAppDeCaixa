using AppDeCaixa.Application.ServiceOrders;

namespace AppDeCaixa.Application.Payments;

public sealed record DebtSettlementAllocation(int PaymentMethodId, int Amount);

public sealed record DebtSettlementDetails(
    int OrderId,
    string? ClientName,
    int CashSessionId,
    long OriginalDebt,
    long PreviouslySettled,
    int SettlementAmount,
    long OutstandingBalance,
    bool DebtCleared,
    IReadOnlyList<OrderPaymentDetails> Payments);

public sealed record OutstandingDebtDetails(
    int OrderId,
    string? ClientName,
    DateTime ClosedAt,
    long OriginalDebt,
    long PreviouslySettled,
    long OutstandingBalance);

public enum DebtSettlementStatus
{
    Success,
    ValidationError,
    NotFound,
    Conflict
}

public sealed record DebtSettlementResult(
    DebtSettlementStatus Status,
    DebtSettlementDetails? Settlement,
    string? Error);

public sealed record OutstandingDebtListResult(
    DebtSettlementStatus Status,
    IReadOnlyList<OutstandingDebtDetails>? Debts,
    string? Error);

public interface IDebtSettlementRepository
{
    Task<IReadOnlyList<OutstandingDebtDetails>> ListOutstandingAsync(
        int companyId,
        string? clientName,
        int limit,
        CancellationToken cancellationToken);

    Task<DebtSettlementResult> RecordAsync(
        int companyId,
        int orderId,
        int userId,
        IReadOnlyList<DebtSettlementAllocation> allocations,
        string? note,
        DateTime recordedAt,
        CancellationToken cancellationToken);
}

public sealed class DebtSettlementService(
    IOrderPaymentRepository paymentMethods,
    IDebtSettlementRepository repository)
{
    public Task<OutstandingDebtListResult> ListOutstandingAsync(
        int companyId,
        string? clientName,
        int limit,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0)
        {
            return Task.FromResult(new OutstandingDebtListResult(
                DebtSettlementStatus.ValidationError, null, "A valid company is required."));
        }

        clientName = string.IsNullOrWhiteSpace(clientName) ? null : clientName.Trim();
        if ((clientName is not null && (clientName.Length is < 2 or > 120)) || limit is < 1 or > 100)
        {
            return Task.FromResult(new OutstandingDebtListResult(
                DebtSettlementStatus.ValidationError, null,
                "Client name must contain 2 to 120 characters, and limit must be between 1 and 100."));
        }

        return ListOutstandingCoreAsync(companyId, clientName, limit, cancellationToken);
    }

    public async Task<DebtSettlementResult> RecordAsync(
        int companyId,
        int orderId,
        int userId,
        IReadOnlyList<DebtSettlementAllocation>? allocations,
        string? note,
        DateTime recordedAt,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || orderId <= 0 || userId <= 0)
        {
            return Invalid("A valid company, order, and acting user are required.");
        }

        if (allocations is null || allocations.Count is < 1 or > 4 ||
            allocations.Any(allocation => allocation is null || allocation.PaymentMethodId <= 0 || allocation.Amount <= 0) ||
            allocations.Select(allocation => allocation.PaymentMethodId).Distinct().Count() != allocations.Count)
        {
            return Invalid("Provide one to four distinct supported payment methods with positive whole-real amounts.");
        }

        var total = allocations.Aggregate(0L, (sum, allocation) => checked(sum + allocation.Amount));
        if (total > int.MaxValue)
        {
            return Invalid("Settlement total exceeds the amount range supported by the database.");
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 500)
        {
            return Invalid("Settlement note cannot exceed 500 characters.");
        }

        foreach (var allocation in allocations)
        {
            var method = await paymentMethods.GetPaymentMethodAsync(allocation.PaymentMethodId, cancellationToken);
            if (method is null || method.RequiresManagerAuthorization)
            {
                return Invalid("Debt can be settled using Pix, Dinheiro, debit card, or credit card only.");
            }
        }

        return await repository.RecordAsync(
            companyId, orderId, userId, allocations, note, recordedAt, cancellationToken);
    }

    private async Task<OutstandingDebtListResult> ListOutstandingCoreAsync(
        int companyId,
        string? clientName,
        int limit,
        CancellationToken cancellationToken)
    {
        var debts = await repository.ListOutstandingAsync(companyId, clientName, limit, cancellationToken);
        return new OutstandingDebtListResult(DebtSettlementStatus.Success, debts, null);
    }

    private static DebtSettlementResult Invalid(string error) =>
        new(DebtSettlementStatus.ValidationError, null, error);
}
