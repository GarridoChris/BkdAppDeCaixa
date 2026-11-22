using System.Security.Claims;
using AppDeCaixa.Application.Collaborators;

namespace AppDeCaixa.Api.Collaborators;

public static class CollaboratorEndpoints
{
    public static IEndpointRouteBuilder MapCollaboratorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/collaborators").WithTags("Collaborators");

        group.MapGet("", async (
                ClaimsPrincipal principal,
                CollaboratorService collaborators,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var results = await collaborators.ListActiveAsync(companyId, cancellationToken);
                return Results.Ok(results);
            })
            .RequireAuthorization(policy => policy.RequireRole("Manager", "Cashier"))
            .WithName("ListActiveCollaborators");

        group.MapPost("", async (
                CreateCollaboratorRequest request,
                ClaimsPrincipal principal,
                CollaboratorService collaborators,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var result = await collaborators.CreateAsync(
                    companyId,
                    request.Name,
                    request.Cpf,
                    request.Email,
                    cancellationToken);

                if (!result.Created)
                {
                    if (result.Conflict)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: result.Error);
                    }

                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["request"] = [result.Error ?? "Invalid collaborator details."]
                    });
                }

                return Results.Created("/api/collaborators", result.Collaborator);
            })
            .RequireAuthorization(policy => policy.RequireRole("Manager"))
            .WithName("CreateCollaborator");

        return endpoints;
    }

    private static bool TryGetCompany(ClaimsPrincipal principal, out int companyId) =>
        int.TryParse(principal.FindFirstValue("company_id"), out companyId) && companyId > 0;

    public sealed record CreateCollaboratorRequest(string? Name, string? Cpf, string? Email);
}
