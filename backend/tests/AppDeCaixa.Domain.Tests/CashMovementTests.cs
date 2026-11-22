using AppDeCaixa.Domain;
using AppDeCaixa.Domain.CashSessions;

namespace AppDeCaixa.Domain.Tests;

public sealed class CashMovementTests
{
    [Fact]
    public void Record_trims_note_and_preserves_audit_details()
    {
        var recordedAt = new DateTime(2026, 11, 1, 14, 30, 0, DateTimeKind.Utc);

        var movement = CashMovement.Record(
            cashSessionId: 7,
            performedByUserId: 12,
            direction: CashMovementDirection.CashOut,
            amount: Money.FromWholeReais(30),
            note: "  Cleaning supplies  ",
            recordedAt: recordedAt);

        Assert.Equal(7, movement.CashSessionId);
        Assert.Equal(12, movement.PerformedByUserId);
        Assert.Equal(CashMovementDirection.CashOut, movement.Direction);
        Assert.Equal(30, movement.Amount.WholeReais);
        Assert.Equal("Cleaning supplies", movement.Note);
        Assert.Equal(DateTimeKind.Unspecified, movement.RecordedAt.Kind);
    }

    [Fact]
    public void Record_rejects_zero_or_negative_amounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CashMovement.Record(
            1, 1, CashMovementDirection.CashIn, Money.Zero, "Change float", DateTime.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => CashMovement.Record(
            1, 1, CashMovementDirection.CashOut, Money.FromWholeReais(-1), "Expense", DateTime.UnixEpoch));
    }

    [Fact]
    public void Record_requires_a_note()
    {
        Assert.Throws<ArgumentException>(() => CashMovement.Record(
            1, 1, CashMovementDirection.CashIn, Money.FromWholeReais(1), " ", DateTime.UnixEpoch));
    }
}
