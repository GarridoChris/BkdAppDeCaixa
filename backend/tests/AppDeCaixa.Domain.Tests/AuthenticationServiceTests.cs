using AppDeCaixa.Application.Authentication;
using AppDeCaixa.Infrastructure.Authentication;

namespace AppDeCaixa.Domain.Tests;

public sealed class AuthenticationServiceTests
{
    [Fact]
    public void Password_hash_service_verifies_password_without_storing_plaintext()
    {
        var hasher = new PasswordHashService();
        var hash = hasher.Hash("A-long-password-123");
        var credential = Credential(isActive: true, role: "Manager") with { PasswordHash = hash };

        Assert.NotEqual("A-long-password-123", hash);
        Assert.True(hasher.Verify(credential, "A-long-password-123"));
        Assert.False(hasher.Verify(credential, "not-the-password"));
    }

    [Fact]
    public async Task Sign_in_rejects_inactive_account_even_when_password_matches()
    {
        var repository = new FakeUserRepository(Credential(isActive: false, role: "Manager"));
        var service = new SignInService(repository, new FakePasswordVerifier(), new FakeTokenIssuer());

        var result = await service.SignInAsync("owner@example.com", "correct", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, repository.LoginRecords);
    }

    [Fact]
    public async Task Sign_in_rejects_users_without_an_assigned_role()
    {
        var repository = new FakeUserRepository(Credential(isActive: true, role: null));
        var service = new SignInService(repository, new FakePasswordVerifier(), new FakeTokenIssuer());

        var result = await service.SignInAsync("owner@example.com", "correct", CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, repository.LoginRecords);
    }

    [Fact]
    public async Task Sign_in_records_login_and_issues_a_token_for_valid_user()
    {
        var repository = new FakeUserRepository(Credential(isActive: true, role: "Cashier"));
        var tokenIssuer = new FakeTokenIssuer();
        var service = new SignInService(repository, new FakePasswordVerifier(), tokenIssuer);

        var result = await service.SignInAsync(" owner@example.com ", "correct", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, repository.LoginRecords);
        Assert.Equal("Cashier", result.User.Role);
        Assert.Equal("token", result.Token.Value);
    }

    [Fact]
    public async Task Bootstrap_rejects_weak_password_without_creating_an_account()
    {
        var repository = new FakeUserRepository(null);
        var service = new BootstrapManagerService(repository, new FakePasswordVerifier());

        var result = await service.CreateFirstManagerAsync(
            "Salon owner", "owner@example.com", "short", 1, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(0, repository.ManagersCreated);
    }

    [Fact]
    public async Task Initial_company_bootstrap_normalizes_cnpj_and_creates_manager_with_hashed_password()
    {
        var repository = new FakeUserRepository(null);
        var service = new InitialCompanyBootstrapService(repository, new FakePasswordVerifier());

        var result = await service.CreateAsync(
            " Example Salon ",
            "12.ABC.678/0001-90",
            " Owner Contact ",
            " Salon Owner ",
            " owner@example.com ",
            "A-long-password-123",
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(99, result.CompanyId);
        Assert.Equal("12ABC678000190", repository.LastCnpj);
        Assert.Equal("OWNER@EXAMPLE.COM", repository.LastEmail);
        Assert.Equal("hash:A-long-password-123", repository.LastPasswordHash);
        Assert.Equal(1, repository.InitialSetupsCreated);
    }

    [Fact]
    public async Task Initial_company_bootstrap_rejects_invalid_cnpj_before_persistence()
    {
        var repository = new FakeUserRepository(null);
        var service = new InitialCompanyBootstrapService(repository, new FakePasswordVerifier());

        var result = await service.CreateAsync(
            "Example Salon", "1234", "Owner Contact", "Salon Owner", "owner@example.com",
            "A-long-password-123", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Null(result.CompanyId);
        Assert.Equal(0, repository.InitialSetupsCreated);
    }

    [Fact]
    public async Task Initial_company_bootstrap_requires_numeric_cnpj_check_digit_positions()
    {
        var repository = new FakeUserRepository(null);
        var service = new InitialCompanyBootstrapService(repository, new FakePasswordVerifier());

        var result = await service.CreateAsync(
            "Example Salon", "12ABC6780001AB", "Owner Contact", "Salon Owner", "owner@example.com",
            "A-long-password-123", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(0, repository.InitialSetupsCreated);
    }

    [Fact]
    public async Task Initial_company_bootstrap_rejects_non_ascii_lookalike_characters()
    {
        var repository = new FakeUserRepository(null);
        var service = new InitialCompanyBootstrapService(repository, new FakePasswordVerifier());

        var result = await service.CreateAsync(
            "Example Salon", "12ıBC678000190", "Owner Contact", "Salon Owner", "owner@example.com",
            "A-long-password-123", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(0, repository.InitialSetupsCreated);
    }

    [Fact]
    public async Task Initial_company_bootstrap_reports_existing_manager_conflict()
    {
        var repository = new FakeUserRepository(null)
        {
            InitialBootstrapStatus = InitialCompanyBootstrapStatus.AlreadyInitialized
        };
        var service = new InitialCompanyBootstrapService(repository, new FakePasswordVerifier());

        var result = await service.CreateAsync(
            "Example Salon", "12345678000190", "Owner Contact", "Salon Owner", "owner@example.com",
            "A-long-password-123", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal("Initial company and Manager setup has already been completed.", result.Error);
    }

    [Fact]
    public async Task Initial_company_bootstrap_does_not_duplicate_an_existing_company()
    {
        var repository = new FakeUserRepository(null)
        {
            InitialBootstrapStatus = InitialCompanyBootstrapStatus.CompanyAlreadyExists
        };
        var service = new InitialCompanyBootstrapService(repository, new FakePasswordVerifier());

        var result = await service.CreateAsync(
            "Example Salon", "12345678000190", "Owner Contact", "Salon Owner", "owner@example.com",
            "A-long-password-123", CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal("A company is already registered. Use the existing-company Manager bootstrap endpoint.", result.Error);
    }

    [Fact]
    public async Task Manager_can_create_employee_only_when_linked_to_active_company_collaborator()
    {
        var repository = new FakeUserRepository(null) { CollaboratorBelongsToCompany = true };
        var service = new CreateUserService(repository, new FakePasswordVerifier());

        var result = await service.CreateAsync(
            1, "Staff Member", "staff@example.com", "a-long-password-123", "Employee", 24, CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal("Employee", result.Role);
        Assert.Equal(1, repository.UsersCreated);
        Assert.Equal(24, repository.LastCreatedCollaboratorId);
    }

    [Fact]
    public async Task Employee_creation_fails_when_collaborator_is_not_active_in_company()
    {
        var repository = new FakeUserRepository(null);
        var service = new CreateUserService(repository, new FakePasswordVerifier());

        var result = await service.CreateAsync(
            1, "Staff Member", "staff@example.com", "a-long-password-123", "Employee", 24, CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(0, repository.UsersCreated);
    }

    private static UserCredential Credential(bool isActive, string? role) => new(
        17, 1, null, "Salon Owner", "owner@example.com", "hash", isActive, role);

    private sealed class FakeUserRepository(UserCredential? credential) : IUserCredentialRepository
    {
        public int LoginRecords { get; private set; }
        public int ManagersCreated { get; private set; }
        public int UsersCreated { get; private set; }
        public bool CollaboratorBelongsToCompany { get; init; }
        public int? LastCreatedCollaboratorId { get; private set; }
        public string? LastCnpj { get; private set; }
        public string? LastEmail { get; private set; }
        public string? LastPasswordHash { get; private set; }
        public int InitialSetupsCreated { get; private set; }
        public InitialCompanyBootstrapStatus InitialBootstrapStatus { get; init; } = InitialCompanyBootstrapStatus.Created;

        public Task<UserCredential?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(credential);

        public Task RecordLoginAsync(int userId, CancellationToken cancellationToken)
        {
            LoginRecords++;
            return Task.CompletedTask;
        }

        public Task<bool> HasManagerAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> CompanyExistsAsync(int companyId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> ActiveCollaboratorBelongsToCompanyAsync(int collaboratorId, int companyId, CancellationToken cancellationToken) =>
            Task.FromResult(CollaboratorBelongsToCompany);
        public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<InitialCompanyBootstrapWriteResult> CreateInitialCompanyAndManagerAsync(
            string companyName,
            string cnpj,
            string contactName,
            string managerName,
            string normalizedEmail,
            string passwordHash,
            CancellationToken cancellationToken)
        {
            InitialSetupsCreated++;
            LastCnpj = cnpj;
            LastEmail = normalizedEmail;
            LastPasswordHash = passwordHash;
            return Task.FromResult(new InitialCompanyBootstrapWriteResult(InitialBootstrapStatus, 99));
        }

        public Task<bool> CreateManagerAsync(string name, string normalizedEmail, int companyId, string passwordHash, CancellationToken cancellationToken)
        {
            ManagersCreated++;
            return Task.FromResult(true);
        }

        public Task<int> CreateUserAsync(string name, string normalizedEmail, string passwordHash, int companyId, string role, int? collaboratorId, CancellationToken cancellationToken)
        {
            UsersCreated++;
            LastCreatedCollaboratorId = collaboratorId;
            return Task.FromResult(42);
        }
    }

    private sealed class FakePasswordVerifier : IPasswordHashService
    {
        public bool Verify(UserCredential credential, string providedPassword) => providedPassword == "correct";
        public string Hash(string password) => $"hash:{password}";
    }

    private sealed class FakeTokenIssuer : IAccessTokenIssuer
    {
        public IssuedAccessToken Issue(AuthenticatedUser user) =>
            new("token", DateTimeOffset.UtcNow.AddMinutes(15));
    }
}
