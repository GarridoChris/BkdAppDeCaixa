using System.Security.Claims;
using AppDeCaixa.Application.Payments;

namespace AppDeCaixa.Api.Payments;

public static class DebtSettlementEndpoints
{
    public static IEndpointRouteBuilder MapDebtSettlementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/debts", async (
                string? clientName,
                int? limit,
                ClaimsPrincipal principal,
                DebtSettlementService settlements,
                CancellationToken cancellationToken) =>
            {
                if (!int.TryParse(principal.FindFirstValue("company_id"), out var companyId) || companyId <= 0)
                {
                    return Results.Forbid();
                }

                var result = await settlements.ListOutstandingAsync(
                    companyId, clientName, limit ?? 50, cancellationToken);
                return result.Status switch
                {
                    DebtSettlementStatus.Success => Results.Ok(result.Debts),
                    DebtSettlementStatus.ValidationError => Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["request"] = [result.Error ?? "Invalid debt query."]
                        }),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                };
            })
            .WithTags("Payments")
            .RequireAuthorization(policy => policy.RequireRole("Manager", "Cashier"))
            .WithName("ListOutstandingFiadoDebts");

        endpoints.MapPost("/api/orders/{orderId:int}/debt-settlements", async (
                int orderId,
                CreateDebtSettlementRequest request,
                ClaimsPrincipal principal,
                BusinessDateProvider businessDate,
                DebtSettlementService settlements,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetIdentity(principal, out var companyId, out var userId))
                {
                    return Results.Forbid();
                }

                if (request.Payments?.Any(payment => payment is null) == true)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["payments"] = ["Each payment allocation must include a payment method and amount."]
                    });
                }

                var allocations = request.Payments?.Select(payment =>
                    new DebtSettlementAllocation(payment!.PaymentMethodId, payment.Amount)).ToArray();
                var result = await settlements.RecordAsync(
                    companyId,
                    orderId,
                    userId,
                    allocations,
                    request.Note,
                    businessDate.LocalNow,
                    cancellationToken);

                return result.Status switch
                {
                    DebtSettlementStatus.Success => Results.Ok(result.Settlement),
                    DebtSettlementStatus.ValidationError => Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["request"] = [result.Error ?? "Invalid debt settlement request."]
                        }),
                    DebtSettlementStatus.NotFound => Results.Problem(
                        statusCode: StatusCodes.Status404NotFound, title: result.Error),
                    DebtSettlementStatus.Conflict => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict, title: result.Error),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                };
            })
            .WithTags("Payments")
            .RequireAuthorization(policy => policy.RequireRole("Manager", "Cashier"))
            .WithName("SettleFiadoDebt");

        return endpoints;
    }

    private static bool TryGetIdentity(ClaimsPrincipal principal, out int companyId, out int userId)
    {
        userId = 0;
        return int.TryParse(principal.FindFirstValue("company_id"), out companyId) && companyId > 0 &&
            int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
    }

    public sealed record CreateDebtSettlementRequest(
        IReadOnlyList<DebtSettlementPaymentRequest?>? Payments,
        string? Note = null);

    public sealed record DebtSettlementPaymentRequest(int PaymentMethodId, int Amount);
}
