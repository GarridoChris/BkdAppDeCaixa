namespace AppDeCaixa.Domain;

/// <summary>Represents a whole number of Brazilian reais.</summary>
public readonly record struct Money
{
    private Money(int wholeReais) => WholeReais = wholeReais;

    public int WholeReais { get; }

    public static Money FromWholeReais(decimal value)
    {
        if (decimal.Truncate(value) != value)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Amounts must be whole reais.");
        }

        if (value is < int.MinValue or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Amount exceeds the database integer range.");
        }

        return new Money((int)value);
    }

    public static Money Zero => new(0);

    public Money Add(Money other) => new(checked(WholeReais + other.WholeReais));

    public Money Subtract(Money other) => new(checked(WholeReais - other.WholeReais));
}
