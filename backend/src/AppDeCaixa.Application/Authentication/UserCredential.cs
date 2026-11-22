namespace AppDeCaixa.Application.Authentication;

public sealed record UserCredential(
    int Id,
    int? CompanyId,
    int? EmployeeId,
    string Name,
    string? Email,
    string PasswordHash,
    bool? IsActive,
    string? Role);

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);

public sealed record AuthenticatedUser(
    int Id,
    int? CompanyId,
    int? EmployeeId,
    string Name,
    string Email,
    string Role);

public sealed record SignInResult(IssuedAccessToken Token, AuthenticatedUser User);
