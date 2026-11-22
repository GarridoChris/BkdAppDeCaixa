namespace AppDeCaixa.Application.ServiceOrders;

public sealed class ServiceOrderItemDetails
{
    public int ItemId { get; set; }
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = "";
    public string ServiceDescription { get; set; } = "";
    public int CollaboratorId { get; set; }
    public string CollaboratorName { get; set; } = "";
    public int CatalogPrice { get; set; }
    public int UnitPrice { get; set; }
    public int CommissionPercentage { get; set; }
    public bool? IsAdditional { get; set; }
    public string? Observation { get; set; }
    public bool? IsPaid { get; set; }
    public string? AdjustmentReason { get; set; }
    public int? AdjustedByUserId { get; set; }
    public DateTime? AdjustedAt { get; set; }
}

public sealed record ServiceOrderDetails(
    int Id,
    string? ClientName,
    int CashSessionId,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    string Status,
    int TotalValue,
    bool? IsDebt,
    long OutstandingDebt,
    int OpenedByUserId,
    IReadOnlyList<ServiceOrderItemDetails> Items,
    IReadOnlyList<OrderPaymentDetails> Payments);

public sealed class OrderPaymentDetails
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int PaymentMethodId { get; set; }
    public string PaymentMethod { get; set; } = "";
    public int Amount { get; set; }
    public DateTime? RecordedAt { get; set; }
    public int? RecordedByUserId { get; set; }
    public string? Note { get; set; }
    public bool IsDebt { get; set; }
    public bool IsDebtSettlement { get; set; }
    public int? CashSessionId { get; set; }
}

public enum ServiceOrderOperationStatus
{
    Success,
    ValidationError,
    NotFound,
    Conflict
}

public sealed record ServiceOrderOperationResult(
    ServiceOrderOperationStatus Status,
    ServiceOrderDetails? Order,
    string? Error);

public interface IServiceOrderRepository
{
    Task<ServiceOrderDetails?> GetByIdAsync(int companyId, int orderId, CancellationToken cancellationToken);
    Task<ServiceOrderOperationResult> OpenAsync(
        int companyId,
        int openedByUserId,
        string clientName,
        DateTime openedAt,
        CancellationToken cancellationToken);
    Task<ServiceOrderOperationResult> AddServiceAsync(
        int companyId,
        int orderId,
        int serviceId,
        int collaboratorId,
        string? observation,
        int adjustedByUserId,
        int? priceOverride,
        string? adjustmentReason,
        DateTime adjustedAt,
        CancellationToken cancellationToken);
}

public sealed class ServiceOrderService(IServiceOrderRepository repository)
{
    public Task<ServiceOrderDetails?> GetByIdAsync(
        int companyId,
        int orderId,
        CancellationToken cancellationToken) =>
        repository.GetByIdAsync(companyId, orderId, cancellationToken);

    public Task<ServiceOrderOperationResult> OpenAsync(
        int companyId,
        int openedByUserId,
        string? clientName,
        DateTime openedAt,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || openedByUserId <= 0)
        {
            return Task.FromResult(new ServiceOrderOperationResult(
                ServiceOrderOperationStatus.ValidationError,
                null,
                "A valid company and user are required."));
        }

        clientName = clientName?.Trim();
        if (string.IsNullOrWhiteSpace(clientName) || clientName.Length > 120)
        {
            return Task.FromResult(new ServiceOrderOperationResult(
                ServiceOrderOperationStatus.ValidationError,
                null,
                "A client name between 1 and 120 characters is required."));
        }

        return repository.OpenAsync(companyId, openedByUserId, clientName, openedAt, cancellationToken);
    }

    public Task<ServiceOrderOperationResult> AddServiceAsync(
        int companyId,
        int orderId,
        int serviceId,
        int collaboratorId,
        string? observation,
        int adjustedByUserId,
        int? priceOverride,
        string? adjustmentReason,
        DateTime adjustedAt,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || orderId <= 0 || serviceId <= 0 || collaboratorId <= 0)
        {
            return Task.FromResult(new ServiceOrderOperationResult(
                ServiceOrderOperationStatus.ValidationError,
                null,
                "A valid order, service, and collaborator are required."));
        }

        observation = string.IsNullOrWhiteSpace(observation) ? null : observation.Trim();
        if (observation?.Length > 500)
        {
            return Task.FromResult(new ServiceOrderOperationResult(
                ServiceOrderOperationStatus.ValidationError,
                null,
                "Observation cannot exceed 500 characters."));
        }

        adjustmentReason = string.IsNullOrWhiteSpace(adjustmentReason) ? null : adjustmentReason.Trim();
        if (priceOverride is < 0)
        {
            return Task.FromResult(new ServiceOrderOperationResult(
                ServiceOrderOperationStatus.ValidationError,
                null,
                "Price override cannot be negative."));
        }

        if (priceOverride is null && adjustmentReason is not null)
        {
            return Task.FromResult(new ServiceOrderOperationResult(
                ServiceOrderOperationStatus.ValidationError,
                null,
                "An adjustment reason can only be supplied with a price override."));
        }

        if (priceOverride is not null && (adjustmentReason is null || adjustmentReason.Length > 500))
        {
            return Task.FromResult(new ServiceOrderOperationResult(
                ServiceOrderOperationStatus.ValidationError,
                null,
                "A price override requires a reason of at most 500 characters."));
        }

        if (adjustedByUserId <= 0)
        {
            return Task.FromResult(new ServiceOrderOperationResult(
                ServiceOrderOperationStatus.ValidationError,
                null,
                "A valid acting user is required."));
        }

        return repository.AddServiceAsync(
            companyId,
            orderId,
            serviceId,
            collaboratorId,
            observation,
            adjustedByUserId,
            priceOverride,
            adjustmentReason,
            adjustedAt,
            cancellationToken);
    }
}
