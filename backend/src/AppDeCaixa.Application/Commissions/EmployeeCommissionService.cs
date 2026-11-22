using AppDeCaixa.Domain.Commissions;

namespace AppDeCaixa.Application.Commissions;

public sealed record EmployeeCommissionLine(
    int OrderId,
    string? ClientName,
    int ServiceOrderItemId,
    int ServiceId,
    string ServiceName,
    DateTime OrderClosedAt,
    int ChargedAmount,
    int CommissionPercentage,
    int CommissionAmount,
    bool IsPaid,
    int? PayoutId,
    DateOnly? PaymentDate,
    string? PaymentMethod);

public sealed record EmployeeCommissionSummary(
    int CollaboratorId,
    string CollaboratorName,
    CommissionPeriodDetails Period,
    int ItemCount,
    long TotalCommission,
    long PaidCommission,
    long UnpaidCommission,
    IReadOnlyList<EmployeeCommissionLine> Items);

public sealed record EmployeeCommissionResult(
    CommissionOperationStatus Status,
    EmployeeCommissionSummary? Summary,
    string? Error);

public interface IEmployeeCommissionRepository
{
    Task<EmployeeCommissionResult> GetOwnCommissionsAsync(
        int companyId,
        int collaboratorId,
        CommissionPeriod period,
        CancellationToken cancellationToken);
}

public sealed class EmployeeCommissionService(IEmployeeCommissionRepository repository)
{
    public Task<EmployeeCommissionResult> GetOwnCommissionsAsync(
        int companyId,
        int collaboratorId,
        int year,
        int month,
        int half,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || collaboratorId <= 0)
        {
            return Task.FromResult(new EmployeeCommissionResult(
                CommissionOperationStatus.ValidationError, null,
                "A valid company and employee profile are required."));
        }

        if (half is < 1 or > 2)
        {
            return Task.FromResult(InvalidPeriod());
        }

        CommissionPeriod period;
        try
        {
            period = CommissionPeriod.Create(year, month,
                half == 1 ? CommissionPeriodPart.FirstHalf : CommissionPeriodPart.SecondHalf);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Task.FromResult(InvalidPeriod());
        }

        return repository.GetOwnCommissionsAsync(companyId, collaboratorId, period, cancellationToken);
    }

    private static EmployeeCommissionResult InvalidPeriod() => new(
        CommissionOperationStatus.ValidationError,
        null,
        "Year, month, and half must identify a valid commission period.");
}
