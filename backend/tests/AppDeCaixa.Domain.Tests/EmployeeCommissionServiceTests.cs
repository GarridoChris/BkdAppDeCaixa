using AppDeCaixa.Application.Commissions;
using AppDeCaixa.Domain.Commissions;

namespace AppDeCaixa.Domain.Tests;

public sealed class EmployeeCommissionServiceTests
{
    [Fact]
    public async Task Rejects_an_invalid_period_without_querying_the_repository()
    {
        var repository = new RecordingEmployeeCommissionRepository();
        var service = new EmployeeCommissionService(repository);

        var result = await service.GetOwnCommissionsAsync(
            companyId: 2,
            collaboratorId: 7,
            year: 2026,
            month: 11,
            half: 3,
            CancellationToken.None);

        Assert.Equal(CommissionOperationStatus.ValidationError, result.Status);
        Assert.Equal(0, repository.CallCount);
    }

    [Fact]
    public async Task Requests_only_the_selected_period_for_the_linked_collaborator()
    {
        var repository = new RecordingEmployeeCommissionRepository();
        var service = new EmployeeCommissionService(repository);

        await service.GetOwnCommissionsAsync(
            companyId: 2,
            collaboratorId: 7,
            year: 2026,
            month: 11,
            half: 2,
            CancellationToken.None);

        Assert.Equal(1, repository.CallCount);
        Assert.Equal(2, repository.CompanyId);
        Assert.Equal(7, repository.CollaboratorId);
        Assert.Equal(new DateOnly(2026, 11, 16), repository.Period?.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 30), repository.Period?.EndDate);
    }

    private sealed class RecordingEmployeeCommissionRepository : IEmployeeCommissionRepository
    {
        public int CallCount { get; private set; }
        public int CompanyId { get; private set; }
        public int CollaboratorId { get; private set; }
        public CommissionPeriod? Period { get; private set; }

        public Task<EmployeeCommissionResult> GetOwnCommissionsAsync(
            int companyId,
            int collaboratorId,
            CommissionPeriod period,
            CancellationToken cancellationToken)
        {
            CallCount++;
            CompanyId = companyId;
            CollaboratorId = collaboratorId;
            Period = period;
            return Task.FromResult(new EmployeeCommissionResult(
                CommissionOperationStatus.Success, null, null));
        }
    }
}
