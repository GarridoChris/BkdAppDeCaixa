using AppDeCaixa.Domain;
using AppDeCaixa.Domain.CashSessions;

namespace AppDeCaixa.Application.CashSessions;

public sealed record CashMovementDetails(
    int Id,
    int CashSessionId,
    string Direction,
    int Amount,
    string Note,
    int PerformedByUserId,
    DateTime RecordedAt);

public sealed record CashSessionReconciliation(
    int CashSessionId,
    string Status,
    int OpeningCash,
    long CashSales,
    long CashIn,
    long CashOut,
    long PixSales,
    long DebitCardSales,
    long CreditCardSales,
    long FiadoDebt,
    long FiadoSettlements,
    long ExpectedCash,
    int? CountedCash,
    long? Difference)
{
    public bool? IsBalanced => Difference is null ? null : Difference == 0;
}

public enum CashMovementOperationStatus
{
    Success,
    ValidationError,
    NotFound,
    Conflict
}

public sealed record CashMovementOperationResult(
    CashMovementOperationStatus Status,
    CashMovementDetails? Movement,
    string? Error);

public interface ICashMovementRepository
{
    Task<CashMovementOperationResult> CreateAsync(
        int companyId,
        CashMovement movement,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CashMovementDetails>?> ListAsync(
        int companyId,
        int cashSessionId,
        CancellationToken cancellationToken);
    Task<CashSessionReconciliation?> GetReconciliationAsync(
        int companyId,
        int cashSessionId,
        CancellationToken cancellationToken);
}

public sealed class CashMovementService(ICashMovementRepository repository)
{
    public async Task<CashMovementOperationResult> RecordAsync(
        int companyId,
        int cashSessionId,
        int performedByUserId,
        string? direction,
        int amount,
        string? note,
        DateTime recordedAt,
        CancellationToken cancellationToken)
    {
        var parsedDirection = direction?.Trim() switch
        {
            string value when string.Equals(value, nameof(CashMovementDirection.CashIn), StringComparison.OrdinalIgnoreCase) =>
                CashMovementDirection.CashIn,
            string value when string.Equals(value, nameof(CashMovementDirection.CashOut), StringComparison.OrdinalIgnoreCase) =>
                CashMovementDirection.CashOut,
            _ => (CashMovementDirection?)null
        };

        if (companyId <= 0 || parsedDirection is null)
        {
            return Invalid("Choose CashIn or CashOut for the movement direction.");
        }

        CashMovement movement;
        try
        {
            movement = CashMovement.Record(
                cashSessionId,
                performedByUserId,
                parsedDirection.Value,
                Money.FromWholeReais(amount),
                note,
                recordedAt);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Invalid(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }

        return await repository.CreateAsync(companyId, movement, cancellationToken);
    }

    public Task<IReadOnlyList<CashMovementDetails>?> ListAsync(
        int companyId,
        int cashSessionId,
        CancellationToken cancellationToken) =>
        repository.ListAsync(companyId, cashSessionId, cancellationToken);

    public Task<CashSessionReconciliation?> GetReconciliationAsync(
        int companyId,
        int cashSessionId,
        CancellationToken cancellationToken) =>
        repository.GetReconciliationAsync(companyId, cashSessionId, cancellationToken);

    private static CashMovementOperationResult Invalid(string error) =>
        new(CashMovementOperationStatus.ValidationError, null, error);
}
