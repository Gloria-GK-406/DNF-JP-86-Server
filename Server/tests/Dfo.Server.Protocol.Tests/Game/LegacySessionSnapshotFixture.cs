using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// Compatibility seam for protocol fixtures that model a selected character's current view as a
/// fixed answer. Whether the session has entered the world is the online identity's answer; tests
/// that need it put the session in the world through <c>TestSessions</c>.
/// </summary>
internal interface ILegacySessionSnapshotFixture : ISelectedCharacterCurrentViewDomainService
{
    bool TryGet(SelectedSessionIdentity session, out SelectCharacterResult? result,
        out bool shouldPersistPosition);

    ValueTask<SelectCharacterResult?> ISelectedCharacterCurrentViewDomainService.TryLoadAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(TryGet(session, out var result, out _) ? result : null);
}
