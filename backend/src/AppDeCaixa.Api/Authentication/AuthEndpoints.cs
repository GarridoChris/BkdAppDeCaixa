using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AppDeCaixa.Application.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace AppDeCaixa.Api.Authentication;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/token", async (
                SignInRequest request,
                SignInService signInService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password) ||
                    request.Email.Length > 254 || request.Password.Length > 1024)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["request"] = ["A valid email and password are required."]
                    });
                }

                var result = await signInService.SignInAsync(request.Email, request.Password, cancellationToken);
                return result is null
                    ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.")
                    : Results.Ok(result);
            })
            .AllowAnonymous()
            .RequireRateLimiting("login")
            .WithName("CreateAccessToken");

        group.MapGet("/me", (ClaimsPrincipal principal) => Results.Ok(new
            {
                id = principal.FindFirstValue(ClaimTypes.NameIdentifier),
                name = principal.Identity?.Name,
                email = principal.FindFirstValue(ClaimTypes.Email),
                role = principal.FindFirstValue(ClaimTypes.Role),
                companyId = principal.FindFirstValue("company_id"),
                employeeId = principal.FindFirstValue("employee_id")
            }))
            .RequireAuthorization()
            .WithName("GetCurrentUser");

        group.MapPost("/bootstrap-manager", async (
                BootstrapManagerRequest request,
                [FromHeader(Name = "X-Admin-Bootstrap-Key")] string? suppliedKey,
                IConfiguration configuration,
                BootstrapManagerService bootstrapService,
                CancellationToken cancellationToken) =>
            {
                var keyFailure = ValidateBootstrapKey(configuration, suppliedKey);
                if (keyFailure is not null)
                {
                    return keyFailure;
                }

                var result = await bootstrapService.CreateFirstManagerAsync(
                    request.Name,
                    request.Email,
                    request.Password,
                    request.CompanyId,
                    cancellationToken);

                if (!result.Created)
                {
                    var status = result.Error == "An administrator account already exists."
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status400BadRequest;
                    return Results.Problem(statusCode: status, title: result.Error);
                }

                return Results.Created("/api/auth/me", new { message = "Initial manager account created." });
            })
            .AllowAnonymous()
            .RequireRateLimiting("bootstrap")
            .WithName("BootstrapInitialManager");

        group.MapPost("/bootstrap-company-manager", async (
                BootstrapCompanyManagerRequest request,
                [FromHeader(Name = "X-Admin-Bootstrap-Key")] string? suppliedKey,
                IConfiguration configuration,
                InitialCompanyBootstrapService bootstrapService,
                CancellationToken cancellationToken) =>
            {
                var keyFailure = ValidateBootstrapKey(configuration, suppliedKey);
                if (keyFailure is not null)
                {
                    return keyFailure;
                }

                var result = await bootstrapService.CreateAsync(
                    request.CompanyName,
                    request.Cnpj,
                    request.ContactName,
                    request.ManagerName,
                    request.ManagerEmail,
                    request.ManagerPassword,
                    cancellationToken);

                if (!result.Created)
                {
                    return Results.Problem(
                        statusCode: result.Conflict ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
                        title: result.Error);
                }

                return Results.Created("/api/auth/me", new
                {
                    companyId = result.CompanyId,
                    message = "Initial company and Manager account created. Sign in to continue."
                });
            })
            .AllowAnonymous()
            .RequireRateLimiting("bootstrap")
            .WithName("BootstrapInitialCompanyAndManager");

        return endpoints;
    }

    private static IResult? ValidateBootstrapKey(IConfiguration configuration, string? suppliedKey)
    {
        var configuredKey = configuration["Bootstrap:AdminKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return Results.NotFound();
        }

        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey ?? "");
        return CryptographicOperations.FixedTimeEquals(configuredBytes, suppliedBytes)
            ? null
            : Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid bootstrap key.");
    }

    public sealed record SignInRequest(string Email, string Password);
    public sealed record BootstrapManagerRequest(string Name, string Email, string Password, int CompanyId);
    public sealed record BootstrapCompanyManagerRequest(
        string? CompanyName,
        string? Cnpj,
        string? ContactName,
        string? ManagerName,
        string? ManagerEmail,
        string? ManagerPassword);
}
