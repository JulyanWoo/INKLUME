using System.Reflection;
using Xunit;

namespace Inklume.Domain.Tests;

public sealed class DomainArchitectureTests
{
    [Fact]
    public void DomainAssembly_ShouldHaveNoInklumeDependencies_WhenLoaded()
    {
        var domainAssembly = Assembly.Load("Inklume.Domain");

        Assert.DoesNotContain(
            domainAssembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Inklume.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void DomainAssembly_ShouldHaveNoPersistenceOrPresentationDependencies_WhenLoaded()
    {
        var domainAssembly = Assembly.Load("Inklume.Domain");

        Assert.DoesNotContain(
            domainAssembly.GetReferencedAssemblies(),
            reference => reference.Name is string name &&
                (name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                 name.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal) ||
                 name is "PresentationCore" or "PresentationFramework" or "WindowsBase" or "System.Xaml"));
    }
}
