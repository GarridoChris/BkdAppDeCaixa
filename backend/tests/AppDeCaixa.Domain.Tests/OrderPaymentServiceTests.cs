using AppDeCaixa.Application.Payments;
using AppDeCaixa.Application.ServiceOrders;

namespace AppDeCaixa.Domain.Tests;

public sealed class OrderPaymentServiceTests
{
    [Theory]
    [InlineData(100, 1, true)]
    [InlineData(101, 1, false)]
    [InlineData(100, 2, false)] // An additional service may have been discounted to zero.
    public void Reviewed_payment_checks_both_value_and_item_count(long total, long items, bool matches)
    {
        Assert.Equal(matches, new ExpectedOrder(100, 1).Matches(total, items));
    }

    [Fact]
    public async Task Fiado_is_forbidden_for_a_cashier()
    {
        var repository = new FakeOrderPaymentRepository(new PaymentMethodOption(5, "Fiado", true));
        var service = new OrderPaymentService(repository);

        var result = await service.RecordFullPaymentAsync(
            companyId: 1,
            orderId: 2,
            userId: 3,
            paymentMethodId: 5,
            isManager: false,
            note: "Approved",
            recordedAt: DateTime.UnixEpoch,
            cancellationToken: CancellationToken.None);

        Assert.Equal(OrderPaymentStatus.Forbidden, result.Status);
        Assert.Equal(0, repository.RecordCalls);
    }

    [Fact]
    public async Task Fiado_requires_an_explanatory_note()
    {
        var repository = new FakeOrderPaymentRepository(new PaymentMethodOption(5, "Fiado", true));
        var service = new OrderPaymentService(repository);

        var result = await service.RecordFullPaymentAsync(
            companyId: 1,
            orderId: 2,
            userId: 3,
            paymentMethodId: 5,
            isManager: true,
            note: "  ",
            recordedAt: DateTime.UnixEpoch,
            cancellationToken: CancellationToken.None);

        Assert.Equal(OrderPaymentStatus.ValidationError, result.Status);
        Assert.Equal(0, repository.RecordCalls);
    }

    [Fact]
    public async Task Manager_can_authorize_fiado_with_a_trimmed_note()
    {
        var repository = new FakeOrderPaymentRepository(new PaymentMethodOption(5, "Fiado", true));
        var service = new OrderPaymentService(repository);

        var result = await service.RecordFullPaymentAsync(
            companyId: 1,
            orderId: 2,
            userId: 3,
            paymentMethodId: 5,
            isManager: true,
            note: "  Pay next Friday  ",
            recordedAt: DateTime.UnixEpoch,
            cancellationToken: CancellationToken.None);

        Assert.Equal(OrderPaymentStatus.Success, result.Status);
        Assert.Equal("Pay next Friday", repository.LastNote);
        Assert.Equal(1, repository.RecordCalls);
    }

    [Fact]
    public async Task Regular_payment_does_not_require_a_note()
    {
        var repository = new FakeOrderPaymentRepository(new PaymentMethodOption(1, "Pix", false));
        var service = new OrderPaymentService(repository);

        var result = await service.RecordFullPaymentAsync(
            companyId: 1,
            orderId: 2,
            userId: 3,
            paymentMethodId: 1,
            isManager: false,
            note: null,
            recordedAt: DateTime.UnixEpoch,
            cancellationToken: CancellationToken.None,
            expectedOrder: new ExpectedOrder(100, 2));

        Assert.Equal(OrderPaymentStatus.Success, result.Status);
        Assert.Equal(new ExpectedOrder(100, 2), repository.LastExpectedOrder);
        Assert.Null(repository.LastNote);
        Assert.Equal(1, repository.RecordCalls);
    }

    private sealed class FakeOrderPaymentRepository(PaymentMethodOption method) : IOrderPaymentRepository
    {
        public int RecordCalls { get; private set; }
        public string? LastNote { get; private set; }
        public ExpectedOrder? LastExpectedOrder { get; private set; }

        public Task<IReadOnlyList<PaymentMethodOption>> ListPaymentMethodsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PaymentMethodOption>>([method]);

        public Task<PaymentMethodOption?> GetPaymentMethodAsync(
            int paymentMethodId,
            CancellationToken cancellationToken) =>
            Task.FromResult<PaymentMethodOption?>(paymentMethodId == method.Id ? method : null);

        public Task<OrderPaymentResult> RecordFullPaymentAsync(
            int companyId,
            int orderId,
            int userId,
            int paymentMethodId,
            bool isManager,
            string? note,
            DateTime recordedAt,
            CancellationToken cancellationToken,
            ExpectedOrder? expectedOrder)
        {
            RecordCalls++;
            LastNote = note;
            LastExpectedOrder = expectedOrder;
            return Task.FromResult(new OrderPaymentResult(
                OrderPaymentStatus.Success,
                new OrderPaymentDetails
                {
                    Id = 1,
                    OrderId = orderId,
                    PaymentMethodId = method.Id,
                    PaymentMethod = method.Name,
                    Amount = 100,
                    RecordedAt = recordedAt,
                    RecordedByUserId = userId,
                    Note = note,
                    IsDebt = method.RequiresManagerAuthorization
                },
                null));
        }
    }
}
