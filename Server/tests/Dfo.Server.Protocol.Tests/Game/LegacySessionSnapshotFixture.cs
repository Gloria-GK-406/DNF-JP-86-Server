using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// Compatibility seam for protocol fixtures that model a selected character as a fixed answer.
/// Production publication stores no such answer; tests may keep their immutable arrangement data.
/// </summary>
internal interface ILegacySessionSnapshotFixture :
    ISelectedSessionRuntimeDirectory, ISelectedCharacterCurrentViewDomainService
{
    bool TryGet(SelectedSessionIdentity session, out SelectCharacterResult? result,
        out bool shouldPersistPosition);

    ValueTask<SelectCharacterResult?> ISelectedCharacterCurrentViewDomainService.TryLoadAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(TryGet(session, out var result, out _) ? result : null);

    void ISelectedSessionRuntimeDirectory.Publish(
        SelectedSessionIdentity session, bool shouldPersistPosition) =>
        throw new NotSupportedException("This fixture only answers.");

    bool ISelectedSessionRuntimeDirectory.IsPublishedExact(SelectedSessionIdentity session) =>
        TryGet(session, out _, out _);

    bool ISelectedSessionRuntimeDirectory.TryGetPositionPersistence(
        SelectedSessionIdentity session, out bool shouldPersistPosition) =>
        TryGet(session, out _, out shouldPersistPosition);
}
