namespace AppDeCaixa.Domain.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_project_has_no_web_or_database_framework_dependencies()
    {
        var repositoryRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (repositoryRoot is not null && !File.Exists(Path.Combine(repositoryRoot.FullName, "AppDeCaixa.slnx")))
        {
            repositoryRoot = repositoryRoot.Parent;
        }

        Assert.NotNull(repositoryRoot);
        var projectFile = Path.Combine(
            repositoryRoot!.FullName,
            "backend/src/AppDeCaixa.Domain/AppDeCaixa.Domain.csproj");

        var project = File.ReadAllText(projectFile);

        Assert.DoesNotContain("Microsoft.AspNetCore", project, StringComparison.Ordinal);
        Assert.DoesNotContain("EntityFrameworkCore", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", project, StringComparison.Ordinal);
    }
}
