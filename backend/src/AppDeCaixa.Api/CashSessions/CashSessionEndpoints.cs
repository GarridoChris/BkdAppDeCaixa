using System.Security.Claims;
using AppDeCaixa.Application.CashSessions;

namespace AppDeCaixa.Api.CashSessions;

public static class CashSessionEndpoints
{
    public static IEndpointRouteBuilder MapCashSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cash-sessions")
            .WithTags("Cash Sessions")
            .RequireAuthorization(policy => policy.RequireRole("Manager", "Cashier"));

        group.MapPost("", async (
                OpenCashSessionRequest request,
                ClaimsPrincipal principal,
                BusinessDateProvider businessDate,
                CashSessionService cashSessions,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetIdentity(principal, out var companyId, out var userId))
                {
                    return Results.Forbid();
                }

                var result = await cashSessions.OpenAsync(
                    companyId,
                    userId,
                    businessDate.Today,
                    request.OpeningCash,
                    cancellationToken);

                return ToHttpResult(result, created: true);
            })
            .WithName("OpenCashSession");

        group.MapGet("/current", async (
                ClaimsPrincipal principal,
                CashSessionService cashSessions,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var session = await cashSessions.GetCurrentAsync(companyId, cancellationToken);
                return session is null ? Results.NotFound() : Results.Ok(session);
            })
            .WithName("GetCurrentCashSession");

        group.MapGet("/{sessionId:int}", async (
                int sessionId,
                ClaimsPrincipal principal,
                CashSessionService cashSessions,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var session = await cashSessions.GetByIdAsync(companyId, sessionId, cancellationToken);
                return session is null ? Results.NotFound() : Results.Ok(session);
            })
            .WithName("GetCashSession");

        group.MapPost("/{sessionId:int}/close", async (
                int sessionId,
                CloseCashSessionRequest request,
                ClaimsPrincipal principal,
                BusinessDateProvider businessDate,
                CashSessionService cashSessions,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetIdentity(principal, out var companyId, out var userId))
                {
                    return Results.Forbid();
                }

                var result = await cashSessions.CloseAsync(
                    companyId,
                    sessionId,
                    userId,
                    businessDate.Today,
                    request.CountedCash,
                    cancellationToken);

                return ToHttpResult(result, created: false);
            })
            .WithName("CloseCashSession");

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

    private static IResult ToHttpResult(CashSessionOperationResult result, bool created) => result.Status switch
    {
        CashSessionOperationStatus.Success when created && result.Session is not null =>
            Results.Created($"/api/cash-sessions/{result.Session.Id}", result.Session),
        CashSessionOperationStatus.Success => Results.Ok(result.Session),
        CashSessionOperationStatus.ValidationError => Results.ValidationProblem(
            new Dictionary<string, string[]> { ["request"] = [result.Error ?? "Invalid request."] }),
        CashSessionOperationStatus.NotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: result.Error),
        CashSessionOperationStatus.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: result.Error),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    public sealed record OpenCashSessionRequest(int OpeningCash);
    public sealed record CloseCashSessionRequest(int CountedCash);
}
