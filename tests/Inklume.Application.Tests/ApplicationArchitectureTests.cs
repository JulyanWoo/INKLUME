using Inklume.Application.Projects;
using Xunit;

namespace Inklume.Application.Tests;

public sealed class ApplicationArchitectureTests
{
    [Fact]
    public void ApplicationAssembly_ShouldNotReferenceInfrastructureOrPresentation_WhenLoaded()
    {
        Assert.DoesNotContain(
            typeof(ProjectService).Assembly.GetReferencedAssemblies(),
            reference => reference.Name is string name &&
                (name is "Inklume.Infrastructure" or "Inklume.Desktop" ||
                 name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                 name.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal) ||
                 name is "PresentationCore" or "PresentationFramework" or "WindowsBase" or "System.Xaml"));
    }
}
