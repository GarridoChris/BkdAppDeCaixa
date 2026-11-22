using AppDeCaixa.Domain.Commissions;

namespace AppDeCaixa.Application.Commissions;

public sealed record CommissionPeriodDetails(
    int Year,
    int Month,
    int Half,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record CommissionLineDetails(
    int ServiceOrderItemId,
    int OrderId,
    string? ClientName,
    int ServiceId,
    string ServiceName,
    DateTime OrderClosedAt,
    int ChargedAmount,
    int CommissionPercentage,
    int CommissionAmount);

public sealed record CommissionPreview(
    int CollaboratorId,
    string CollaboratorName,
    CommissionPeriodDetails Period,
    int ItemCount,
    int TotalAmount,
    IReadOnlyList<CommissionLineDetails> Items);

public sealed record CommissionPayoutDetails(
    int Id,
    int CollaboratorId,
    string CollaboratorName,
    CommissionPeriodDetails Period,
    DateOnly PaymentDate,
    int TotalAmount,
    string PaymentMethod,
    string? Note,
    int RecordedByUserId,
    DateTime RecordedAt,
    IReadOnlyList<CommissionLineDetails> Items);

public enum CommissionOperationStatus
{
    Success,
    ValidationError,
    NotFound,
    Conflict
}

public sealed record CommissionPreviewResult(
    CommissionOperationStatus Status,
    CommissionPreview? Preview,
    string? Error);

public sealed record CommissionPayoutResult(
    CommissionOperationStatus Status,
    CommissionPayoutDetails? Payout,
    string? Error);

public interface ICommissionRepository
{
    Task<CommissionPreviewResult> GetEligibleAsync(
        int companyId,
        int collaboratorId,
        CommissionPeriod period,
        CancellationToken cancellationToken);

    Task<CommissionPayoutResult> CreatePayoutAsync(
        int companyId,
        int collaboratorId,
        int userId,
        CommissionPeriod period,
        string paymentMethod,
        string? note,
        DateOnly paymentDate,
        DateTime recordedAt,
        CancellationToken cancellationToken);
}

public sealed class CommissionService(ICommissionRepository repository)
{
    public Task<CommissionPreviewResult> GetEligibleAsync(
        int companyId,
        int collaboratorId,
        int year,
        int month,
        int half,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || collaboratorId <= 0)
        {
            return Task.FromResult(new CommissionPreviewResult(
                CommissionOperationStatus.ValidationError, null, "A valid company and collaborator are required."));
        }

        if (!TryCreatePeriod(year, month, half, out var period))
        {
            return Task.FromResult(new CommissionPreviewResult(
                CommissionOperationStatus.ValidationError, null, "Year, month, and half must identify a valid commission period."));
        }

        return repository.GetEligibleAsync(companyId, collaboratorId, period, cancellationToken);
    }

    public Task<CommissionPayoutResult> CreatePayoutAsync(
        int companyId,
        int collaboratorId,
        int userId,
        int year,
        int month,
        int half,
        string? paymentMethod,
        string? note,
        DateOnly paymentDate,
        DateTime recordedAt,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || collaboratorId <= 0 || userId <= 0)
        {
            return Task.FromResult(new CommissionPayoutResult(
                CommissionOperationStatus.ValidationError, null, "A valid company, collaborator, and user are required."));
        }

        if (!TryCreatePeriod(year, month, half, out var period))
        {
            return Task.FromResult(new CommissionPayoutResult(
                CommissionOperationStatus.ValidationError, null, "Year, month, and half must identify a valid commission period."));
        }

        var normalizedMethod = NormalizePaymentMethod(paymentMethod);
        if (normalizedMethod is null)
        {
            return Task.FromResult(new CommissionPayoutResult(
                CommissionOperationStatus.ValidationError, null, "Commission payouts support only Pix or Dinheiro."));
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 500)
        {
            return Task.FromResult(new CommissionPayoutResult(
                CommissionOperationStatus.ValidationError, null, "Payout note cannot exceed 500 characters."));
        }

        return repository.CreatePayoutAsync(
            companyId, collaboratorId, userId, period, normalizedMethod, note,
            paymentDate, recordedAt, cancellationToken);
    }

    public static CommissionPeriodDetails Describe(CommissionPeriod period) => new(
        period.Year,
        period.Month,
        period.Part == CommissionPeriodPart.FirstHalf ? 1 : 2,
        period.StartDate,
        period.EndDate);

    private static bool TryCreatePeriod(int year, int month, int half, out CommissionPeriod period)
    {
        if (half is < 1 or > 2)
        {
            period = default;
            return false;
        }

        try
        {
            period = CommissionPeriod.Create(year, month,
                half == 1 ? CommissionPeriodPart.FirstHalf : CommissionPeriodPart.SecondHalf);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            period = default;
            return false;
        }
    }

    private static string? NormalizePaymentMethod(string? paymentMethod)
    {
        if (string.Equals(paymentMethod?.Trim(), "Pix", StringComparison.OrdinalIgnoreCase))
        {
            return "Pix";
        }

        return string.Equals(paymentMethod?.Trim(), "Dinheiro", StringComparison.OrdinalIgnoreCase)
            ? "Dinheiro"
            : null;
    }
}
