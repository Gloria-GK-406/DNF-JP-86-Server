namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>
/// Characterization hosts each open the real archive; running them one at a time keeps a loaded
/// machine from turning a startup budget into a false failure.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CharacterizationCollection
{
    public const string Name = "Characterization";

    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(120);
}
