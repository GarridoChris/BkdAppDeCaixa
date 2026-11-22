using AppDeCaixa.Application.Payments;
using AppDeCaixa.Application.ServiceOrders;

namespace AppDeCaixa.Domain.Tests;

public sealed class DebtSettlementServiceTests
{
    [Fact]
    public async Task Records_partial_split_allocations_and_trims_the_note()
    {
        var methods = new FakeOrderPaymentRepository(
            new PaymentMethodOption(1, "Pix", false),
            new PaymentMethodOption(2, "Dinheiro", false));
        var repository = new FakeDebtSettlementRepository();
        var service = new DebtSettlementService(methods, repository);

        var result = await service.RecordAsync(
            companyId: 3,
            orderId: 12,
            userId: 7,
            allocations: [new(1, 30), new(2, 20)],
            note: "  First installment  ",
            recordedAt: DateTime.UnixEpoch,
            CancellationToken.None);

        Assert.Equal(DebtSettlementStatus.Success, result.Status);
        Assert.Equal("First installment", repository.Note);
        Assert.Equal(new[] { new DebtSettlementAllocation(1, 30), new DebtSettlementAllocation(2, 20) },
            repository.Allocations);
    }

    [Fact]
    public async Task Rejects_Fiado_as_a_settlement_method()
    {
        var methods = new FakeOrderPaymentRepository(new PaymentMethodOption(5, "Fiado", true));
        var repository = new FakeDebtSettlementRepository();
        var service = new DebtSettlementService(methods, repository);

        var result = await service.RecordAsync(
            companyId: 3,
            orderId: 12,
            userId: 7,
            allocations: [new(5, 30)],
            note: null,
            recordedAt: DateTime.UnixEpoch,
            CancellationToken.None);

        Assert.Equal(DebtSettlementStatus.ValidationError, result.Status);
        Assert.Equal(0, repository.RecordCalls);
    }

    [Fact]
    public async Task Rejects_duplicate_methods_and_nonpositive_amounts()
    {
        var methods = new FakeOrderPaymentRepository(new PaymentMethodOption(1, "Pix", false));
        var repository = new FakeDebtSettlementRepository();
        var service = new DebtSettlementService(methods, repository);

        var duplicate = await service.RecordAsync(
            3, 12, 7, [new(1, 30), new(1, 20)], null, DateTime.UnixEpoch, CancellationToken.None);
        var nonpositive = await service.RecordAsync(
            3, 12, 7, [new(1, 0)], null, DateTime.UnixEpoch, CancellationToken.None);

        Assert.Equal(DebtSettlementStatus.ValidationError, duplicate.Status);
        Assert.Equal(DebtSettlementStatus.ValidationError, nonpositive.Status);
        Assert.Equal(0, repository.RecordCalls);
    }

    [Fact]
    public async Task Lists_debts_for_the_exact_trimmed_client_name_and_valid_limit()
    {
        var paymentMethods = new FakeOrderPaymentRepository();
        var repository = new FakeDebtSettlementRepository();
        var service = new DebtSettlementService(paymentMethods, repository);

        var result = await service.ListOutstandingAsync(3, "  Maria Silva  ", 25, CancellationToken.None);

        Assert.Equal(DebtSettlementStatus.Success, result.Status);
        Assert.Equal(1, repository.ListCalls);
        Assert.Equal(3, repository.ListCompanyId);
        Assert.Equal("Maria Silva", repository.ClientName);
        Assert.Equal(25, repository.Limit);
    }

    [Fact]
    public async Task Rejects_invalid_debt_list_filters()
    {
        var repository = new FakeDebtSettlementRepository();
        var service = new DebtSettlementService(new FakeOrderPaymentRepository(), repository);

        var result = await service.ListOutstandingAsync(3, "A", 101, CancellationToken.None);

        Assert.Equal(DebtSettlementStatus.ValidationError, result.Status);
        Assert.Equal(0, repository.ListCalls);
    }

    private sealed class FakeOrderPaymentRepository(params PaymentMethodOption[] methods) : IOrderPaymentRepository
    {
        public Task<IReadOnlyList<PaymentMethodOption>> ListPaymentMethodsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PaymentMethodOption>>(methods);

        public Task<PaymentMethodOption?> GetPaymentMethodAsync(
            int paymentMethodId,
            CancellationToken cancellationToken) =>
            Task.FromResult(methods.SingleOrDefault(method => method.Id == paymentMethodId));

        public Task<OrderPaymentResult> RecordFullPaymentAsync(
            int companyId,
            int orderId,
            int userId,
            int paymentMethodId,
            bool isManager,
            string? note,
            DateTime recordedAt,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDebtSettlementRepository : IDebtSettlementRepository
    {
        public int RecordCalls { get; private set; }
        public int ListCalls { get; private set; }
        public int ListCompanyId { get; private set; }
        public string? ClientName { get; private set; }
        public int Limit { get; private set; }
        public IReadOnlyList<DebtSettlementAllocation> Allocations { get; private set; } = [];
        public string? Note { get; private set; }

        public Task<IReadOnlyList<OutstandingDebtDetails>> ListOutstandingAsync(
            int companyId,
            string? clientName,
            int limit,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            ListCompanyId = companyId;
            ClientName = clientName;
            Limit = limit;
            return Task.FromResult<IReadOnlyList<OutstandingDebtDetails>>([]);
        }

        public Task<DebtSettlementResult> RecordAsync(
            int companyId,
            int orderId,
            int userId,
            IReadOnlyList<DebtSettlementAllocation> allocations,
            string? note,
            DateTime recordedAt,
            CancellationToken cancellationToken)
        {
            RecordCalls++;
            Allocations = allocations;
            Note = note;
            return Task.FromResult(new DebtSettlementResult(DebtSettlementStatus.Success, null, null));
        }
    }
}
