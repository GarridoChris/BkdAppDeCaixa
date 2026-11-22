namespace AppDeCaixa.Application.Collaborators;

public sealed record CollaboratorDetails(int Id, int CompanyId, string Name, bool? IsActive);

public sealed record CreateCollaboratorResult(CollaboratorDetails? Collaborator, string? Error, bool Conflict = false)
{
    public bool Created => Collaborator is not null;
}

public interface ICollaboratorRepository
{
    Task<IReadOnlyList<CollaboratorDetails>> ListActiveAsync(int companyId, CancellationToken cancellationToken);
    Task<CreateCollaboratorResult> CreateAsync(
        int companyId,
        string name,
        string? cpf,
        string? email,
        CancellationToken cancellationToken);
}

public sealed class CollaboratorService(ICollaboratorRepository repository)
{
    public Task<IReadOnlyList<CollaboratorDetails>> ListActiveAsync(
        int companyId,
        CancellationToken cancellationToken) =>
        repository.ListActiveAsync(companyId, cancellationToken);

    public Task<CreateCollaboratorResult> CreateAsync(
        int companyId,
        string? name,
        string? cpf,
        string? email,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || string.IsNullOrWhiteSpace(name) || name.Trim().Length is < 2 or > 120)
        {
            return Task.FromResult(new CreateCollaboratorResult(
                null,
                "A valid company and a name between 2 and 120 characters are required."));
        }

        name = name.Trim();
        cpf = string.IsNullOrWhiteSpace(cpf) ? null : cpf.Trim();
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

        if (cpf?.Length > 20)
        {
            return Task.FromResult(new CreateCollaboratorResult(null, "CPF cannot exceed 20 characters."));
        }

        if (email is not null &&
            (email.Length > 254 ||
             !System.Net.Mail.MailAddress.TryCreate(email, out var address) ||
             !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(new CreateCollaboratorResult(null, "Enter a valid email address."));
        }

        return repository.CreateAsync(companyId, name, cpf, email, cancellationToken);
    }
}
