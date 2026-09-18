using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Tests.Game;

internal sealed class FixedCurrentViews : ISelectedCharacterCurrentViewDomainService
{
    private readonly Dictionary<SelectedSessionIdentity, SelectCharacterResult> entries = [];

    public int LoadCount { get; private set; }

    public void Publish(SelectedSessionIdentity session, SelectCharacterResult result) =>
        entries[session] = result;

    public ValueTask<SelectCharacterResult?> TryLoadAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken)
    {
        LoadCount++;
        return ValueTask.FromResult(entries.GetValueOrDefault(session));
    }
}
