using AppDeCaixa.Domain.Users;

namespace AppDeCaixa.Application.Authentication;

public interface IUserCredentialRepository
{
    Task<UserCredential?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task RecordLoginAsync(int userId, CancellationToken cancellationToken);
    Task<bool> HasManagerAsync(CancellationToken cancellationToken);
    Task<bool> CompanyExistsAsync(int companyId, CancellationToken cancellationToken);
    Task<InitialCompanyBootstrapWriteResult> CreateInitialCompanyAndManagerAsync(
        string companyName,
        string cnpj,
        string contactName,
        string managerName,
        string normalizedEmail,
        string passwordHash,
        CancellationToken cancellationToken);
    Task<bool> ActiveCollaboratorBelongsToCompanyAsync(int collaboratorId, int companyId, CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<bool> CreateManagerAsync(string name, string normalizedEmail, int companyId, string passwordHash, CancellationToken cancellationToken);
    Task<int> CreateUserAsync(
        string name,
        string normalizedEmail,
        string passwordHash,
        int companyId,
        string role,
        int? collaboratorId,
        CancellationToken cancellationToken);
}

public interface IPasswordVerifier
{
    bool Verify(UserCredential credential, string providedPassword);
}

public interface IPasswordHashService : IPasswordVerifier
{
    string Hash(string password);
}

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(AuthenticatedUser user);
}

public sealed record BootstrapManagerResult(bool Created, string? Error);
public sealed record CreateUserResult(bool Created, int? UserId, string? Error, string? Role);
public enum InitialCompanyBootstrapStatus
{
    Created,
    AlreadyInitialized,
    CompanyAlreadyExists,
    EmailAlreadyExists
}

public sealed record InitialCompanyBootstrapWriteResult(InitialCompanyBootstrapStatus Status, int? CompanyId);
public sealed record InitialCompanyBootstrapResult(bool Created, int? CompanyId, string? Error, bool Conflict = false);

public sealed class InitialCompanyBootstrapService(
    IUserCredentialRepository repository,
    IPasswordHashService passwordHasher)
{
    public async Task<InitialCompanyBootstrapResult> CreateAsync(
        string? companyName,
        string? cnpj,
        string? contactName,
        string? managerName,
        string? managerEmail,
        string? managerPassword,
        CancellationToken cancellationToken)
    {
        companyName = companyName?.Trim();
        cnpj = NormalizeCnpj(cnpj);
        contactName = contactName?.Trim();
        managerName = managerName?.Trim();
        managerEmail = managerEmail?.Trim().ToUpperInvariant();

        if (companyName is null or { Length: < 2 or > 160 } ||
            contactName is null or { Length: < 2 or > 160 } ||
            cnpj is null ||
            managerName is null or { Length: < 2 or > 120 } ||
            managerEmail is null or { Length: 0 or > 254 } ||
            string.IsNullOrEmpty(managerPassword))
        {
            return new InitialCompanyBootstrapResult(false, null, "Company name, a 14-character CNPJ, contact name, Manager name, email, and password are required.");
        }

        if (!System.Net.Mail.MailAddress.TryCreate(managerEmail, out var parsedAddress) ||
            !string.Equals(parsedAddress.Address, managerEmail, StringComparison.OrdinalIgnoreCase))
        {
            return new InitialCompanyBootstrapResult(false, null, "Enter a valid Manager email address.");
        }

        if (managerPassword.Length is < 12 or > 1024)
        {
            return new InitialCompanyBootstrapResult(false, null, "The initial password must contain between 12 and 1024 characters.");
        }

        var writeResult = await repository.CreateInitialCompanyAndManagerAsync(
            companyName,
            cnpj,
            contactName,
            managerName,
            managerEmail,
            passwordHasher.Hash(managerPassword),
            cancellationToken);

        return writeResult.Status switch
        {
            InitialCompanyBootstrapStatus.Created => new InitialCompanyBootstrapResult(true, writeResult.CompanyId, null),
            InitialCompanyBootstrapStatus.AlreadyInitialized => new InitialCompanyBootstrapResult(false, null, "Initial company and Manager setup has already been completed.", true),
            InitialCompanyBootstrapStatus.CompanyAlreadyExists => new InitialCompanyBootstrapResult(false, null, "A company is already registered. Use the existing-company Manager bootstrap endpoint.", true),
            InitialCompanyBootstrapStatus.EmailAlreadyExists => new InitialCompanyBootstrapResult(false, null, "That email address is already assigned to an account.", true),
            _ => new InitialCompanyBootstrapResult(false, null, "Initial setup could not be completed.")
        };
    }

    private static string? NormalizeCnpj(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = value.Trim()
            .Replace(".", "", StringComparison.Ordinal)
            .Replace("/", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal);

        if (compact.Length != 14 || !compact.All(IsAsciiAlphaNumeric))
        {
            return null;
        }

        var normalized = compact.ToUpperInvariant();
        return
            normalized[..12].All(IsAsciiAlphaNumeric) &&
            normalized[12..].All(IsAsciiDigit)
            ? normalized
            : null;
    }

    private static bool IsAsciiAlphaNumeric(char character) =>
        character is >= '0' and <= '9' or >= 'A' and <= 'Z';

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';
}

public sealed class CreateUserService(
    IUserCredentialRepository repository,
    IPasswordHashService passwordHasher)
{
    public async Task<CreateUserResult> CreateAsync(
        int companyId,
        string? name,
        string? email,
        string? password,
        string? roleName,
        int? collaboratorId,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password) ||
            string.IsNullOrWhiteSpace(roleName))
        {
            return new CreateUserResult(false, null, "Company, name, email, password, and role are required.", null);
        }

        name = name.Trim();
        email = email.Trim().ToUpperInvariant();
        if (name.Length is < 2 or > 120 ||
            !System.Net.Mail.MailAddress.TryCreate(email, out var parsedAddress) ||
            !string.Equals(parsedAddress.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            return new CreateUserResult(false, null, "Enter a valid name and email address.", null);
        }

        if (password.Length is < 12 or > 1024)
        {
            return new CreateUserResult(false, null, "Password must contain between 12 and 1024 characters.", null);
        }

        if (!Enum.TryParse<UserRole>(roleName, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            return new CreateUserResult(false, null, "Role must be Manager, Cashier, or Employee.", null);
        }

        if (role == UserRole.Employee)
        {
            if (collaboratorId is null ||
                !await repository.ActiveCollaboratorBelongsToCompanyAsync(collaboratorId.Value, companyId, cancellationToken))
            {
                return new CreateUserResult(false, null, "Employee accounts must link to an active collaborator in this company.", null);
            }
        }
        else if (collaboratorId is not null)
        {
            return new CreateUserResult(false, null, "Only Employee accounts can link to a collaborator.", null);
        }

        if (await repository.EmailExistsAsync(email, cancellationToken))
        {
            return new CreateUserResult(false, null, "That email address is already assigned to an account.", null);
        }

        var userId = await repository.CreateUserAsync(
            name,
            email,
            passwordHasher.Hash(password),
            companyId,
            role.ToString(),
            collaboratorId,
            cancellationToken);

        return new CreateUserResult(true, userId, null, role.ToString());
    }
}

public sealed class BootstrapManagerService(
    IUserCredentialRepository repository,
    IPasswordHashService passwordHasher)
{
    public async Task<BootstrapManagerResult> CreateFirstManagerAsync(
        string? name,
        string? email,
        string? password,
        int companyId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return new BootstrapManagerResult(false, "Name, email, and password are required.");
        }

        name = name.Trim();
        email = email.Trim().ToUpperInvariant();
        if (name.Length is < 2 or > 120 ||
            !System.Net.Mail.MailAddress.TryCreate(email, out var parsedAddress) ||
            !string.Equals(parsedAddress.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            return new BootstrapManagerResult(false, "Enter a valid name and email address.");
        }

        if (password.Length < 12)
        {
            return new BootstrapManagerResult(false, "The initial password must contain at least 12 characters.");
        }

        if (companyId <= 0 || !await repository.CompanyExistsAsync(companyId, cancellationToken))
        {
            return new BootstrapManagerResult(false, "The selected company does not exist.");
        }

        if (await repository.HasManagerAsync(cancellationToken))
        {
            return new BootstrapManagerResult(false, "An administrator account already exists.");
        }

        if (await repository.EmailExistsAsync(email, cancellationToken))
        {
            return new BootstrapManagerResult(false, "That email address is already assigned to an account.");
        }

        var created = await repository.CreateManagerAsync(
            name,
            email,
            companyId,
            passwordHasher.Hash(password),
            cancellationToken);
        return created
            ? new BootstrapManagerResult(true, null)
            : new BootstrapManagerResult(false, "An administrator account already exists.");
    }
}

public sealed class SignInService(
    IUserCredentialRepository repository,
    IPasswordVerifier passwordVerifier,
    IAccessTokenIssuer tokenIssuer)
{
    public async Task<SignInResult?> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToUpperInvariant();
        if (normalizedEmail.Length == 0 || password.Length == 0)
        {
            return null;
        }

        var credential = await repository.FindByEmailAsync(normalizedEmail, cancellationToken);
        if (credential is null || credential.IsActive != true ||
            credential.Email is null || credential.Role is null ||
            !Enum.TryParse<UserRole>(credential.Role, ignoreCase: true, out var role) ||
            !Enum.IsDefined(role) ||
            !passwordVerifier.Verify(credential, password))
        {
            return null;
        }

        var user = new AuthenticatedUser(
            credential.Id,
            credential.CompanyId,
            credential.EmployeeId,
            credential.Name,
            credential.Email,
            role.ToString());

        await repository.RecordLoginAsync(credential.Id, cancellationToken);
        return new SignInResult(tokenIssuer.Issue(user), user);
    }
}
