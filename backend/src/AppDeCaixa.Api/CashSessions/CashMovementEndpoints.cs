using System.Security.Claims;
using AppDeCaixa.Application.CashSessions;

namespace AppDeCaixa.Api.CashSessions;

public static class CashMovementEndpoints
{
    public static IEndpointRouteBuilder MapCashMovementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cash-sessions/{sessionId:int}")
            .WithTags("Cash Sessions")
            .RequireAuthorization(policy => policy.RequireRole("Manager", "Cashier"));

        group.MapPost("/movements", async (
                int sessionId,
                RecordCashMovementRequest request,
                ClaimsPrincipal principal,
                BusinessDateProvider businessDate,
                CashMovementService movements,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetIdentity(principal, out var companyId, out var userId))
                {
                    return Results.Forbid();
                }

                var result = await movements.RecordAsync(
                    companyId,
                    sessionId,
                    userId,
                    request.Direction,
                    request.Amount,
                    request.Note,
                    businessDate.LocalNow,
                    cancellationToken);

                return ToHttpResult(result, sessionId);
            })
            .WithName("RecordCashMovement");

        group.MapGet("/movements", async (
                int sessionId,
                ClaimsPrincipal principal,
                CashMovementService movements,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var records = await movements.ListAsync(companyId, sessionId, cancellationToken);
                return records is null ? Results.NotFound() : Results.Ok(records);
            })
            .WithName("ListCashMovements");

        group.MapGet("/reconciliation", async (
                int sessionId,
                ClaimsPrincipal principal,
                CashMovementService movements,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var reconciliation = await movements.GetReconciliationAsync(companyId, sessionId, cancellationToken);
                return reconciliation is null ? Results.NotFound() : Results.Ok(reconciliation);
            })
            .WithName("GetCashSessionReconciliation");

        return endpoints;
    }

    private static bool TryGetIdentity(ClaimsPrincipal principal, out int companyId, out int userId)
    {
        userId = 0;
        return TryGetCompany(principal, out companyId) &&
            int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId) &&
            userId > 0;
    }

    private static bool TryGetCompany(ClaimsPrincipal principal, out int companyId) =>
        int.TryParse(principal.FindFirstValue("company_id"), out companyId) && companyId > 0;

    private static IResult ToHttpResult(CashMovementOperationResult result, int sessionId) => result.Status switch
    {
        CashMovementOperationStatus.Success when result.Movement is not null =>
            Results.Created($"/api/cash-sessions/{sessionId}/movements", result.Movement),
        CashMovementOperationStatus.ValidationError => Results.ValidationProblem(
            new Dictionary<string, string[]> { ["request"] = [result.Error ?? "Invalid cash movement."] }),
        CashMovementOperationStatus.NotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: result.Error),
        CashMovementOperationStatus.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: result.Error),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    public sealed record RecordCashMovementRequest(string? Direction, int Amount, string? Note);
}
