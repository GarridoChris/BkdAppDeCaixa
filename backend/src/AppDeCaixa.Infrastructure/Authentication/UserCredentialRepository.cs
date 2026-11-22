using AppDeCaixa.Application.Authentication;
using AppDeCaixa.Infrastructure.Persistence;
using AppDeCaixa.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AppDeCaixa.Infrastructure.Authentication;

public sealed class UserCredentialRepository(AppDbContext dbContext) : IUserCredentialRepository
{
    private const long InitialBootstrapLockId = 7261653501;

    public async Task<UserCredential?> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.Email != null && candidate.Email.ToUpper() == normalizedEmail,
            cancellationToken);

        return user is null
            ? null
            : new UserCredential(
                user.Id,
                user.CompanyId,
                user.EmployeeId,
                user.Name,
                user.Email,
                user.PasswordHash,
                user.IsActive,
                user.Role);
    }

    public async Task RecordLoginAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == userId, cancellationToken);
        // The existing column is timestamp without time zone. Store UTC wall-clock ticks until it is migrated.
        user.LastLogin = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasManagerAsync(CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(user => user.Role == "Manager" && user.IsActive == true, cancellationToken);

    public Task<bool> CompanyExistsAsync(int companyId, CancellationToken cancellationToken) =>
        dbContext.Set<CompanyRecord>().AnyAsync(company => company.Id == companyId, cancellationToken);

    public async Task<InitialCompanyBootstrapWriteResult> CreateInitialCompanyAndManagerAsync(
        string companyName,
        string cnpj,
        string contactName,
        string managerName,
        string normalizedEmail,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireInitialBootstrapLockAsync(cancellationToken);

        if (await HasManagerAsync(cancellationToken))
        {
            return new InitialCompanyBootstrapWriteResult(InitialCompanyBootstrapStatus.AlreadyInitialized, null);
        }

        if (await dbContext.Set<CompanyRecord>().AnyAsync(cancellationToken))
        {
            return new InitialCompanyBootstrapWriteResult(InitialCompanyBootstrapStatus.CompanyAlreadyExists, null);
        }

        if (await EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            return new InitialCompanyBootstrapWriteResult(InitialCompanyBootstrapStatus.EmailAlreadyExists, null);
        }

        var company = new CompanyRecord
        {
            Name = companyName,
            Cnpj = cnpj,
            Contact = contactName
        };
        dbContext.Set<CompanyRecord>().Add(company);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.Users.Add(new UserRecord
        {
            Name = managerName,
            Email = normalizedEmail,
            CompanyId = company.Id,
            PasswordHash = passwordHash,
            IsActive = true,
            Role = "Manager"
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new InitialCompanyBootstrapWriteResult(InitialCompanyBootstrapStatus.Created, company.Id);
    }

    public Task<bool> ActiveCollaboratorBelongsToCompanyAsync(
        int collaboratorId,
        int companyId,
        CancellationToken cancellationToken) =>
        dbContext.Set<CollaboratorRecord>().AnyAsync(
            collaborator => collaborator.Id == collaboratorId &&
                collaborator.CompanyId == companyId &&
                collaborator.IsActive == true,
            cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(
            user => user.Email != null && user.Email.ToUpper() == normalizedEmail,
            cancellationToken);

    public async Task<bool> CreateManagerAsync(
        string name,
        string normalizedEmail,
        int companyId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquireInitialBootstrapLockAsync(cancellationToken);

        if (await HasManagerAsync(cancellationToken))
        {
            return false;
        }

        dbContext.Users.Add(new UserRecord
        {
            Name = name,
            Email = normalizedEmail,
            CompanyId = companyId,
            PasswordHash = passwordHash,
            IsActive = true,
            Role = "Manager"
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> CreateUserAsync(
        string name,
        string normalizedEmail,
        string passwordHash,
        int companyId,
        string role,
        int? collaboratorId,
        CancellationToken cancellationToken)
    {
        var user = new UserRecord
        {
            Name = name,
            Email = normalizedEmail,
            CompanyId = companyId,
            EmployeeId = collaboratorId,
            PasswordHash = passwordHash,
            IsActive = true,
            Role = role
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    private async Task AcquireInitialBootstrapLockAsync(CancellationToken cancellationToken) =>
        await dbContext.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock({InitialBootstrapLockId}::bigint);",
            cancellationToken);
}

public sealed class PasswordHashService : IPasswordHashService
{
    private readonly PasswordHasher<UserRecord> _hasher = new();

    public bool Verify(UserCredential credential, string providedPassword)
    {
        var user = new UserRecord { Name = credential.Name, PasswordHash = credential.PasswordHash };
        try
        {
            return _hasher.VerifyHashedPassword(user, credential.PasswordHash, providedPassword)
                != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public string Hash(string password)
    {
        var user = new UserRecord { Name = "", PasswordHash = "" };
        return _hasher.HashPassword(user, password);
    }
}
