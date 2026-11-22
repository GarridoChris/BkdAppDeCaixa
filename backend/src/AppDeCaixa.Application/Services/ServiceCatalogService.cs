using AppDeCaixa.Domain.Services;

namespace AppDeCaixa.Application.Services;

public sealed record ServiceDetails(
    int Id,
    int CompanyId,
    string Name,
    string Description,
    int CommissionPercentage,
    int Price,
    bool? IsActive);

public sealed record CreateServiceResult(ServiceDetails? Service, string? Error)
{
    public bool Created => Service is not null;
}

public interface IServiceCatalogRepository
{
    Task<IReadOnlyList<ServiceDetails>> ListActiveAsync(int companyId, CancellationToken cancellationToken);
    Task<ServiceDetails> CreateAsync(ServiceDefinition definition, CancellationToken cancellationToken);
}

public sealed class ServiceCatalogService(IServiceCatalogRepository repository)
{
    public Task<IReadOnlyList<ServiceDetails>> ListActiveAsync(int companyId, CancellationToken cancellationToken) =>
        repository.ListActiveAsync(companyId, cancellationToken);

    public async Task<CreateServiceResult> CreateAsync(
        int companyId,
        string? name,
        string? description,
        int commissionPercentage,
        int price,
        CancellationToken cancellationToken)
    {
        ServiceDefinition definition;
        try
        {
            definition = ServiceDefinition.Create(
                companyId,
                name,
                description,
                commissionPercentage,
                price);
        }
        catch (ArgumentException exception)
        {
            return new CreateServiceResult(null, exception.Message);
        }

        var service = await repository.CreateAsync(definition, cancellationToken);
        return new CreateServiceResult(service, null);
    }
}
