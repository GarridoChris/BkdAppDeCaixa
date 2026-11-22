using AppDeCaixa.Application.ServiceOrders;

namespace AppDeCaixa.Application.Payments;

public sealed record PaymentMethodOption(int Id, string Name, bool RequiresManagerAuthorization);

public enum OrderPaymentStatus
{
    Success,
    ValidationError,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record OrderPaymentResult(
    OrderPaymentStatus Status,
    OrderPaymentDetails? Payment,
    string? Error);

public interface IOrderPaymentRepository
{
    Task<IReadOnlyList<PaymentMethodOption>> ListPaymentMethodsAsync(CancellationToken cancellationToken);
    Task<PaymentMethodOption?> GetPaymentMethodAsync(int paymentMethodId, CancellationToken cancellationToken);
    Task<OrderPaymentResult> RecordFullPaymentAsync(
        int companyId,
        int orderId,
        int userId,
        int paymentMethodId,
        bool isManager,
        string? note,
        DateTime recordedAt,
        CancellationToken cancellationToken);
}

public sealed class OrderPaymentService(IOrderPaymentRepository repository)
{
    public Task<IReadOnlyList<PaymentMethodOption>> ListPaymentMethodsAsync(CancellationToken cancellationToken) =>
        repository.ListPaymentMethodsAsync(cancellationToken);

    public async Task<OrderPaymentResult> RecordFullPaymentAsync(
        int companyId,
        int orderId,
        int userId,
        int paymentMethodId,
        bool isManager,
        string? note,
        DateTime recordedAt,
        CancellationToken cancellationToken)
    {
        if (companyId <= 0 || orderId <= 0 || userId <= 0 || paymentMethodId <= 0)
        {
            return Invalid("A valid company, order, user, and payment method are required.");
        }

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 500)
        {
            return Invalid("Payment note cannot exceed 500 characters.");
        }

        var method = await repository.GetPaymentMethodAsync(paymentMethodId, cancellationToken);
        if (method is null)
        {
            return Invalid("Choose a supported payment method.");
        }

        if (method.RequiresManagerAuthorization && !isManager)
        {
            return new OrderPaymentResult(OrderPaymentStatus.Forbidden, null, "Only a Manager can authorize Fiado.");
        }

        if (method.RequiresManagerAuthorization && note is null)
        {
            return Invalid("A note is required when authorizing Fiado.");
        }

        return await repository.RecordFullPaymentAsync(
            companyId,
            orderId,
            userId,
            paymentMethodId,
            isManager,
            note,
            recordedAt,
            cancellationToken);
    }

    private static OrderPaymentResult Invalid(string error) =>
        new(OrderPaymentStatus.ValidationError, null, error);
}
