using AppDeCaixa.Application.Services;
using AppDeCaixa.Domain.Services;
using AppDeCaixa.Infrastructure.Persistence;
using AppDeCaixa.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AppDeCaixa.Infrastructure.Services;

public sealed class ServiceCatalogRepository(AppDbContext dbContext) : IServiceCatalogRepository
{
    public async Task<IReadOnlyList<ServiceDetails>> ListActiveAsync(
        int companyId,
        CancellationToken cancellationToken) =>
        await dbContext.Services
            .AsNoTracking()
            .Where(service => service.CompanyId == companyId && service.IsActive == true)
            .OrderBy(service => service.Name)
            .Select(service => new ServiceDetails(
                service.Id,
                service.CompanyId ?? 0,
                service.Name,
                service.Description,
                service.CommissionPercentage,
                service.Price,
                service.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<ServiceDetails> CreateAsync(
        ServiceDefinition definition,
        CancellationToken cancellationToken)
    {
        var record = new ServiceCatalogRecord
        {
            CompanyId = definition.CompanyId,
            Name = definition.Name,
            Description = definition.Description,
            CommissionPercentage = definition.CommissionPercentage,
            Price = definition.Price.WholeReais,
            IsActive = true
        };

        dbContext.Services.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ServiceDetails(
            record.Id,
            record.CompanyId ?? 0,
            record.Name,
            record.Description,
            record.CommissionPercentage,
            record.Price,
            record.IsActive);
    }
}
