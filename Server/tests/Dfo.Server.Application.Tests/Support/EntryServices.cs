using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Builds a <see cref="CharacterEntryService"/> for tests, defaulting every collaborator a
/// test is not interested in.
/// </summary>
/// <remarks>
/// The entry service composes many limbs, so its constructor grows as limbs are added.
/// Funnelling construction through here means a new limb changes one line rather than
/// every test that ever built one, and it keeps each test's argument list down to the
/// collaborators that test is actually about.
/// </remarks>
internal static class EntryServices
{
    public static CharacterEntryService Create(
        ICharacterService characters,
        IInventoryEntryService inventory,
        ICharacterEntryStateService characterEntries,
        IAccountEntryStateService accountEntries,
        IActiveCharacterService activeCharacters,
        IWorldContentEntryService? worldContent = null,
        IChannelEntryPolicy? channels = null,
        IEntryPlacementService? placement = null,
        ISessionAdjacentEntryService? sessionAdjacent = null,
        TimeProvider? timeProvider = null) =>
        new(characters,
            inventory,
            characterEntries,
            accountEntries,
            worldContent ?? new StubWorldContentEntryService(),
            channels ?? new StubChannelEntryPolicy(),
            placement ?? new StubEntryPlacementService(),
            sessionAdjacent ?? new StubSessionAdjacentEntryService(),
            activeCharacters,
            timeProvider ?? TimeProvider.System);
}
