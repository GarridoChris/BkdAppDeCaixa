namespace AppDeCaixa.Infrastructure.Persistence.Entities;

public sealed class UserRecord
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int? EmployeeId { get; set; }
    public required string Name { get; set; }
    public string? Cpf { get; set; }
    public string? Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime? LastLogin { get; set; }
    public DateTime? LastLogout { get; set; }
    public bool? IsActive { get; set; }
    public string? Role { get; set; }
}

public sealed class CompanyRecord
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Cnpj { get; set; }
    public required string Contact { get; set; }
}

public sealed class CollaboratorRecord
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public required string Name { get; set; }
    public string? Cpf { get; set; }
    public string? Email { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class CashSessionRecord
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public DateOnly OpenedOn { get; set; }
    public DateOnly? ClosedOn { get; set; }
    public int? OpenedByUserId { get; set; }
    public int? ClosedByUserId { get; set; }
    public int OpeningCash { get; set; }
    public int? ClosingCash { get; set; }
    public int? TotalEntries { get; set; }
    public int? TotalOutflows { get; set; }
    public string? Status { get; set; }
}

public sealed class CashMovementRecord
{
    public int Id { get; set; }
    public int CashSessionId { get; set; }
    public required string Direction { get; set; }
    public int Amount { get; set; }
    public required string Note { get; set; }
    public int PerformedByUserId { get; set; }
    public DateTime RecordedAt { get; set; }
}

public sealed class ServiceOrderRecord
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public string? ClientName { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Status { get; set; }
    public int? TotalValue { get; set; }
    public bool? IsDebt { get; set; }
    public int? OpenedByUserId { get; set; }
    public int? ClosedByUserId { get; set; }
    public int? CashSessionId { get; set; }
}

public sealed class ServiceCatalogRecord
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int CommissionPercentage { get; set; }
    public int Price { get; set; }
    public bool? IsActive { get; set; }
}
