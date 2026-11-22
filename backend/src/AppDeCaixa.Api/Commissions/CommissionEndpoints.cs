using System.Security.Claims;
using AppDeCaixa.Application.Commissions;

namespace AppDeCaixa.Api.Commissions;

public static class CommissionEndpoints
{
    public static IEndpointRouteBuilder MapCommissionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/collaborators/{collaboratorId:int}")
            .WithTags("Commissions")
            .RequireAuthorization(policy => policy.RequireRole("Manager"));

        group.MapGet("/commissions", async (
                int collaboratorId,
                int year,
                int month,
                int half,
                ClaimsPrincipal principal,
                CommissionService commissions,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var result = await commissions.GetEligibleAsync(
                    companyId, collaboratorId, year, month, half, cancellationToken);
                return ToHttpResult(result);
            })
            .WithName("GetCollaboratorCommissions");

        group.MapPost("/commission-payouts", async (
                int collaboratorId,
                CreateCommissionPayoutRequest request,
                ClaimsPrincipal principal,
                BusinessDateProvider businessDate,
                CommissionService commissions,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetIdentity(principal, out var companyId, out var userId))
                {
                    return Results.Forbid();
                }

                var result = await commissions.CreatePayoutAsync(
                    companyId,
                    collaboratorId,
                    userId,
                    request.Year,
                    request.Month,
                    request.Half,
                    request.PaymentMethod,
                    request.Note,
                    businessDate.Today,
                    businessDate.LocalNow,
                    cancellationToken);

                return ToHttpResult(result);
            })
            .WithName("CreateCollaboratorCommissionPayout");

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

    private static IResult ToHttpResult(CommissionPreviewResult result) => result.Status switch
    {
        CommissionOperationStatus.Success => Results.Ok(result.Preview),
        CommissionOperationStatus.ValidationError => ValidationError(result.Error),
        CommissionOperationStatus.NotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound, title: result.Error),
        CommissionOperationStatus.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict, title: result.Error),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    private static IResult ToHttpResult(CommissionPayoutResult result) => result.Status switch
    {
        CommissionOperationStatus.Success => Results.Ok(result.Payout),
        CommissionOperationStatus.ValidationError => ValidationError(result.Error),
        CommissionOperationStatus.NotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound, title: result.Error),
        CommissionOperationStatus.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict, title: result.Error),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    private static IResult ValidationError(string? error) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["request"] = [error ?? "Invalid commission request."] });

    public sealed record CreateCommissionPayoutRequest(
        int Year,
        int Month,
        int Half,
        string? PaymentMethod,
        string? Note = null);
}
