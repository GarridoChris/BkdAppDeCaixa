using System.Security.Claims;
using AppDeCaixa.Application.ServiceOrders;

namespace AppDeCaixa.Api.ServiceOrders;

public static class ServiceOrderEndpoints
{
    public static IEndpointRouteBuilder MapServiceOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/orders")
            .WithTags("Service Orders")
            .RequireAuthorization(policy => policy.RequireRole("Manager", "Cashier"));

        group.MapPost("", async (
                OpenServiceOrderRequest request,
                ClaimsPrincipal principal,
                BusinessDateProvider businessDate,
                ServiceOrderService serviceOrders,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetIdentity(principal, out var companyId, out var userId))
                {
                    return Results.Forbid();
                }

                var result = await serviceOrders.OpenAsync(
                    companyId,
                    userId,
                    request.ClientName,
                    businessDate.LocalNow,
                    cancellationToken);

                return ToHttpResult(result, created: true);
            })
            .WithName("OpenServiceOrder");

        group.MapGet("", async (
                string? status,
                ClaimsPrincipal principal,
                ServiceOrderService serviceOrders,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                if (status is not null && !string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["status"] = ["Only the open status is supported."]
                    });
                }

                return Results.Ok(await serviceOrders.ListOpenAsync(companyId, cancellationToken));
            })
            .WithName("ListOpenServiceOrders");

        group.MapGet("/{orderId:int}", async (
                int orderId,
                ClaimsPrincipal principal,
                ServiceOrderService serviceOrders,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetCompany(principal, out var companyId))
                {
                    return Results.Forbid();
                }

                var order = await serviceOrders.GetByIdAsync(companyId, orderId, cancellationToken);
                return order is null ? Results.NotFound() : Results.Ok(order);
            })
            .WithName("GetServiceOrder");

        group.MapPost("/{orderId:int}/items", async (
                int orderId,
                AddServiceToOrderRequest request,
                ClaimsPrincipal principal,
                BusinessDateProvider businessDate,
                ServiceOrderService serviceOrders,
                CancellationToken cancellationToken) =>
            {
                if (!TryGetIdentity(principal, out var companyId, out var userId))
                {
                    return Results.Forbid();
                }

                var result = await serviceOrders.AddServiceAsync(
                    companyId,
                    orderId,
                    request.ServiceId,
                    request.CollaboratorId,
                    request.Observation,
                    userId,
                    request.PriceOverride,
                    request.AdjustmentReason,
                    businessDate.LocalNow,
                    cancellationToken);

                return ToHttpResult(result, created: false);
            })
            .WithName("AddServiceToOrder");

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

    private static IResult ToHttpResult(ServiceOrderOperationResult result, bool created) => result.Status switch
    {
        ServiceOrderOperationStatus.Success when created && result.Order is not null =>
            Results.Created($"/api/orders/{result.Order.Id}", result.Order),
        ServiceOrderOperationStatus.Success => Results.Ok(result.Order),
        ServiceOrderOperationStatus.ValidationError => Results.ValidationProblem(
            new Dictionary<string, string[]> { ["request"] = [result.Error ?? "Invalid request."] }),
        ServiceOrderOperationStatus.NotFound => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: result.Error),
        ServiceOrderOperationStatus.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: result.Error),
        _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
    };

    public sealed record AddServiceToOrderRequest(
        int ServiceId,
        int CollaboratorId,
        string? Observation = null,
        int? PriceOverride = null,
        string? AdjustmentReason = null);

    public sealed record OpenServiceOrderRequest(string? ClientName);
}
