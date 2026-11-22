using AppDeCaixa.Application.Collaborators;
using AppDeCaixa.Infrastructure.Persistence;
using AppDeCaixa.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AppDeCaixa.Infrastructure.Collaborators;

public sealed class CollaboratorRepository(AppDbContext dbContext) : ICollaboratorRepository
{
    private const string CpfUniqueConstraint = "colaborador_cpf_key";

    public async Task<IReadOnlyList<CollaboratorDetails>> ListActiveAsync(
        int companyId,
        CancellationToken cancellationToken) =>
        await dbContext.Set<CollaboratorRecord>()
            .AsNoTracking()
            .Where(collaborator => collaborator.CompanyId == companyId && collaborator.IsActive == true)
            .OrderBy(collaborator => collaborator.Name)
            .Select(collaborator => new CollaboratorDetails(
                collaborator.Id,
                collaborator.CompanyId ?? 0,
                collaborator.Name,
                collaborator.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<CreateCollaboratorResult> CreateAsync(
        int companyId,
        string name,
        string? cpf,
        string? email,
        CancellationToken cancellationToken)
    {
        var collaborator = new CollaboratorRecord
        {
            CompanyId = companyId,
            Name = name,
            Cpf = cpf,
            Email = email,
            IsActive = true
        };

        dbContext.Set<CollaboratorRecord>().Add(collaborator);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateCpf(exception))
        {
            dbContext.Entry(collaborator).State = EntityState.Detached;
            return new CreateCollaboratorResult(null, "That CPF is already assigned to a collaborator.", Conflict: true);
        }

        return new CreateCollaboratorResult(
            new CollaboratorDetails(collaborator.Id, companyId, collaborator.Name, collaborator.IsActive),
            null);
    }

    private static bool IsDuplicateCpf(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: CpfUniqueConstraint
        };
}
