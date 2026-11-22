using System.Security.Claims;
using AppDeCaixa.Application.Payments;

namespace AppDeCaixa.Api.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api")
            .WithTags("Payments")
            .RequireAuthorization(policy => policy.RequireRole("Manager", "Cashier"));

        group.MapGet("/payment-methods", async (
                OrderPaymentService payments,
                CancellationToken cancellationToken) =>
            Results.Ok(await payments.ListPaymentMethodsAsync(cancellationToken)))
            .WithName("ListPaymentMethods");

        group.MapPost("/orders/{orderId:int}/payments", async (
                int orderId,
                RecordOrderPaymentRequest request,
                ClaimsPrincipal principal,
                BusinessDateProvider businessDate,
                OrderPaymentService payments,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetIdentity(principal, out var companyId, out var userId))
                {
                    return Results.Forbid();
                }

                var result = await payments.RecordFullPaymentAsync(
                    companyId,
                    orderId,
                    userId,
                    request.PaymentMethodId,
                    principal.IsInRole("Manager"),
                    request.Note,
                    businessDate.LocalNow,
                    cancellationToken);

                return ToHttpResult(result);
            })
            .WithName("RecordFullOrderPayment");

        return endpoints;
    }

    private static bool TryGetIdentity(ClaimsPrincipal principal, out int companyId, out int userId)
    {
        userId = 0;
        return int.TryParse(principal.FindFirstValue("company_id"), out companyId) && companyId > 0 &&
            int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
    }

    private static IResult ToHttpResult(OrderPaymentResult result) => result.Status switch
    {
        OrderPaymentStatus.Success => Results.Ok(result.Payment),
        OrderPaymentStatus.ValidationError => Results.ValidationProblem(
            new Dictionary<string, string[]> { ["request"] = [result.Error ?? "Invalid payment request."] }),
        OrderPaymentStatus.Forbidden => Results.Forbid(),
        OrderPaymentStatus.NotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: result.Error),
        OrderPaymentStatus.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: result.Error),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    public sealed record RecordOrderPaymentRequest(int PaymentMethodId, string? Note = null);
}
