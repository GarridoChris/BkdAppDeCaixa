using System.Security.Claims;
using AppDeCaixa.Application.Services;

namespace AppDeCaixa.Api.Services;

public static class ServiceEndpoints
{
    public static IEndpointRouteBuilder MapServiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/services").WithTags("Services");

        group.MapGet("", async (
                ClaimsPrincipal principal,
                ServiceCatalogService catalog,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var services = await catalog.ListActiveAsync(companyId, cancellationToken);
                return Results.Ok(services);
            })
            .RequireAuthorization(policy => policy.RequireRole("Manager", "Cashier"))
            .WithName("ListActiveServices");

        group.MapPost("", async (
                CreateServiceRequest request,
                ClaimsPrincipal principal,
                ServiceCatalogService catalog,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var result = await catalog.CreateAsync(
                    companyId,
                    request.Name,
                    request.Description,
                    request.CommissionPercentage,
                    request.Price,
                    cancellationToken);

                if (!result.Created)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["request"] = [result.Error ?? "Invalid service details."]
                    });
                }

                return Results.Created("/api/services", result.Service);
            })
            .RequireAuthorization(policy => policy.RequireRole("Manager"))
            .WithName("CreateService");

        return endpoints;
    }

    private static bool TryGetCompany(ClaimsPrincipal principal, out int companyId) =>
        int.TryParse(principal.FindFirstValue("company_id"), out companyId) && companyId > 0;

    public sealed record CreateServiceRequest(
        string? Name,
        string? Description,
        int CommissionPercentage,
        int Price);
}
