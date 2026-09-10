namespace Dfo.Launcher.Configuration;

public interface ILauncherConfigurationSource
{
    LauncherConfiguration Read();
}

public sealed class LauncherConfigurationFileSource(string path) : ILauncherConfigurationSource
{
    public LauncherConfiguration Read() => LauncherConfigurationParser.Load(path);
}
