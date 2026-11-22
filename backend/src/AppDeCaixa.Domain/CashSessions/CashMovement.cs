namespace AppDeCaixa.Domain.CashSessions;

public enum CashMovementDirection
{
    CashIn,
    CashOut
}

public sealed class CashMovement
{
    private CashMovement(
        int cashSessionId,
        int performedByUserId,
        CashMovementDirection direction,
        Money amount,
        string note,
        DateTime recordedAt)
    {
        CashSessionId = cashSessionId;
        PerformedByUserId = performedByUserId;
        Direction = direction;
        Amount = amount;
        Note = note;
        RecordedAt = DateTime.SpecifyKind(recordedAt, DateTimeKind.Unspecified);
    }

    public int CashSessionId { get; }
    public int PerformedByUserId { get; }
    public CashMovementDirection Direction { get; }
    public Money Amount { get; }
    public string Note { get; }
    public DateTime RecordedAt { get; }

    public static CashMovement Record(
        int cashSessionId,
        int performedByUserId,
        CashMovementDirection direction,
        Money amount,
        string? note,
        DateTime recordedAt)
    {
        if (cashSessionId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cashSessionId));
        }

        if (performedByUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(performedByUserId));
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (amount.WholeReais <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Cash movement amount must be greater than zero.");
        }

        note = note?.Trim();
        if (string.IsNullOrWhiteSpace(note) || note.Length > 500)
        {
            throw new ArgumentException("A note of 1 to 500 characters is required.", nameof(note));
        }

        return new CashMovement(cashSessionId, performedByUserId, direction, amount, note, recordedAt);
    }
}
