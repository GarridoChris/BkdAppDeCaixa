using System.Security.Claims;
using AppDeCaixa.Application.Commissions;

namespace AppDeCaixa.Api.Commissions;

public static class EmployeeCommissionEndpoints
{
    public static IEndpointRouteBuilder MapEmployeeCommissionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/commissions", async (
                int year,
                int month,
                int half,
                ClaimsPrincipal principal,
                EmployeeCommissionService commissions,
                CancellationToken cancellationToken) =>
            {
                if (!int.TryParse(principal.FindFirstValue("company_id"), out var companyId) || companyId <= 0 ||
                    !int.TryParse(principal.FindFirstValue("employee_id"), out var collaboratorId) || collaboratorId <= 0)
                {
                    return Results.Forbid();
                }

                var result = await commissions.GetOwnCommissionsAsync(
                    companyId, collaboratorId, year, month, half, cancellationToken);
                return result.Status switch
                {
                    CommissionOperationStatus.Success => Results.Ok(result.Summary),
                    CommissionOperationStatus.ValidationError => Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["request"] = [result.Error ?? "Invalid commission period."]
                        }),
                    CommissionOperationStatus.NotFound => Results.Problem(
                        statusCode: StatusCodes.Status404NotFound, title: result.Error),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                };
            })
            .WithTags("Employee Self-Service")
            .RequireAuthorization(policy => policy.RequireRole("Employee"))
            .WithName("GetOwnCommissions");

        return endpoints;
    }
}
