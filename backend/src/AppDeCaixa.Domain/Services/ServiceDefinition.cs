namespace AppDeCaixa.Domain.Services;

public sealed class ServiceDefinition
{
    private ServiceDefinition(
        int companyId,
        string name,
        string description,
        int commissionPercentage,
        Money price)
    {
        CompanyId = companyId;
        Name = name;
        Description = description;
        CommissionPercentage = commissionPercentage;
        Price = price;
    }

    public int CompanyId { get; }
    public string Name { get; }
    public string Description { get; }
    public int CommissionPercentage { get; }
    public Money Price { get; }

    public static ServiceDefinition Create(
        int companyId,
        string? name,
        string? description,
        int commissionPercentage,
        int wholeReaisPrice)
    {
        if (companyId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(companyId));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            throw new ArgumentException("Service name is required and must be at most 120 characters.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 2000)
        {
            throw new ArgumentException("Service description is required and must be at most 2000 characters.", nameof(description));
        }

        if (commissionPercentage is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(commissionPercentage), "Commission percentage must be between 0 and 100.");
        }

        var price = Money.FromWholeReais(wholeReaisPrice);
        if (price.WholeReais < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(wholeReaisPrice), "Service price cannot be negative.");
        }

        return new ServiceDefinition(
            companyId,
            name.Trim(),
            description.Trim(),
            commissionPercentage,
            price);
    }
}
