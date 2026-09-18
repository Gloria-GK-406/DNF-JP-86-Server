using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

public interface ISelectCharacterPreparationDomainService
{
    /// <summary>Builds a one-shot view from current domain state without selection-time mutations.</summary>
    ValueTask<SelectCharacterResult> LoadCurrentAsync(
        SelectedSessionIdentity session, CancellationToken cancellationToken);
    ValueTask<ActiveCharacterState> PreflightAsync(
        AccountId accountId, ushort rosterIndex, ChannelEntryIdentity channel,
        CancellationToken cancellationToken);
    ValueTask<SelectCharacterResult> PrepareAsync(
        ActiveCharacterState selected,
        AccountId accountId,
        ChannelEntryIdentity channel,
        CancellationToken cancellationToken);

    /// <summary>
    /// Puts the prepared character at the channel's spawn, and records it where the channel says
    /// positions are kept.
    /// </summary>
    /// <remarks>
    /// Resolving the spawn, writing it onto the character, and storing it are one act, so they
    /// live behind one call. They used to be three steps in the caller, which meant the caller
    /// held the storage seam and could perform two of the three without the third.
    /// </remarks>
    ValueTask<SelectCharacterResult> PlaceAtSpawnAsync(
        SelectedSessionIdentity session,
        SelectCharacterResult prepared,
        CancellationToken cancellationToken);
}
