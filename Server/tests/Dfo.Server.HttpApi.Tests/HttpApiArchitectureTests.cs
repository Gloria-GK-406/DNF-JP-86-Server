using System.Xml.Linq;

namespace Dfo.Server.HttpApi.Tests;

public sealed class HttpApiArchitectureTests
{
    [Fact]
    public void HttpApiDoesNotReferencePersistenceInfrastructureNetworkingOrProtocol()
    {
        var project = XDocument.Load(FindProjectFile());
        var references = project.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(((string)element.Attribute("Include")!).Replace('\\', '/')))
            .ToArray();

        Assert.Equal(
            ["Dfo.Server.Abstractions", "Dfo.Server.Application", "Dfo.Server.Data"],
            references.Order(StringComparer.Ordinal).ToArray());
    }

    private static string FindProjectFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Dfo.Server.HttpApi",
                "Dfo.Server.HttpApi.csproj");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the HttpApi project file.");
    }
}
