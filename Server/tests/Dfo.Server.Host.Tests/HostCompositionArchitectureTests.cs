using System.Xml.Linq;

namespace Dfo.Server.Host.Tests;

public sealed class HostCompositionArchitectureTests
{
    [Fact]
    public void HostDependsOnEntryComponentsRatherThanConcreteGameTransportAndProtocol()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "86JP.Server.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var project = XDocument.Load(Path.Combine(directory.FullName, "src", "Dfo.Server.Host", "Dfo.Server.Host.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(node => Path.GetFileNameWithoutExtension(((string)node.Attribute("Include")!).Replace('\\', '/')))
            .ToArray();
        Assert.Contains("Dfo.Server.GameServer", references);
        Assert.Contains("Dfo.Server.HttpApi", references);
        Assert.DoesNotContain("Dfo.Server.Networking", references);
        Assert.DoesNotContain("Dfo.Server.Protocol", references);
    }
}
