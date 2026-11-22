using System.Security.Claims;
using AppDeCaixa.Application.Authentication;

namespace AppDeCaixa.Api.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/users", async (
                CreateUserRequest request,
                ClaimsPrincipal principal,
                CreateUserService createUser,
                CancellationToken cancellationToken) =>
            {
                if (!int.TryParse(principal.FindFirstValue("company_id"), out var companyId))
                {
                    return Results.Forbid();
                }

                var result = await createUser.CreateAsync(
                    companyId,
                    request.Name,
                    request.Email,
                    request.Password,
                    request.Role,
                    request.EmployeeId,
                    cancellationToken);

                if (!result.Created)
                {
                    var statusCode = result.Error == "That email address is already assigned to an account."
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status400BadRequest;
                    return Results.Problem(statusCode: statusCode, title: result.Error);
                }

                return Results.Json(
                    new { id = result.UserId, role = result.Role },
                    statusCode: StatusCodes.Status201Created);
            })
            .WithTags("Users")
            .RequireAuthorization(policy => policy.RequireRole("Manager"))
            .WithName("CreateUser");

        return endpoints;
    }

    public sealed record CreateUserRequest(
        string? Name,
        string? Email,
        string? Password,
        string? Role,
        int? EmployeeId);
}
