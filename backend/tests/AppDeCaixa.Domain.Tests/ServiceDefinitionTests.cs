using AppDeCaixa.Domain.Services;

namespace AppDeCaixa.Domain.Tests;

public sealed class ServiceDefinitionTests
{
    [Fact]
    public void Create_trims_name_and_description_and_keeps_whole_real_values()
    {
        var service = ServiceDefinition.Create(1, "  Haircut  ", "  Wash and cut  ", 40, 80);

        Assert.Equal(1, service.CompanyId);
        Assert.Equal("Haircut", service.Name);
        Assert.Equal("Wash and cut", service.Description);
        Assert.Equal(40, service.CommissionPercentage);
        Assert.Equal(80, service.Price.WholeReais);
    }

    [Fact]
    public void Create_requires_a_name_and_description()
    {
        Assert.Throws<ArgumentException>(() =>
            ServiceDefinition.Create(1, " ", "Description", 40, 80));
        Assert.Throws<ArgumentException>(() =>
            ServiceDefinition.Create(1, "Name", " ", 40, 80));
    }

    [Fact]
    public void Create_rejects_commission_outside_zero_to_one_hundred()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ServiceDefinition.Create(1, "Haircut", "Description", 101, 80));
    }

    [Fact]
    public void Create_rejects_negative_service_price()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ServiceDefinition.Create(1, "Haircut", "Description", 40, -1));
    }
}
