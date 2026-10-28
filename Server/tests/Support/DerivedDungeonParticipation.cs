using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Testing;

/// <summary>
/// A tower stand-in that can name a character's run without a session, as the production
/// registry does: it keeps a seat by character even after a newer session generation of that
/// character has replaced the seated one.
/// </summary>
internal interface ICharacterTowerSeats
{
    Guid? RunOf(CharacterId character);
}

/// <summary>
/// The dungeon occupancy registry read off the dungeon services a fixture already holds, so the
/// registry and the services can never disagree by setup mistake.
/// </summary>
/// <remarks>
/// A character is registered in the Boss tower when that tower seats it, else in the illusion
/// tower, else in the ordinary run the run service finds by character. A tower stand-in that is
/// not an <see cref="ICharacterTowerSeats"/> is asked through the character's current session,
/// which is all such a stand-in can answer; without a session directory it never registers.
/// <see cref="Residual"/> holds what the services can no longer show, such as an ordinary
/// registration left after a party departure that has not yet ended the character's part.
/// Registry writes belong to the services' own entry and exit, so they are refused here.
/// </remarks>
internal sealed class DerivedDungeonParticipation(
    IBossTowerDomainService bossTowers,
    IIllusionTowerDomainService illusionTowers,
    IDungeonRunDomainService runs,
    ISelectedSessionDomainService? sessions = null) : IDungeonParticipationDomainService
{
    public Dictionary<CharacterId, DungeonParticipationOwner> Residual { get; } = [];

    public DungeonParticipationOwner? Find(CharacterId character) =>
        BossRunOf(character) is { } boss ? new(DungeonParticipationMode.BossTower, boss)
        : IllusionRunOf(character) is { } illusion ? new(DungeonParticipationMode.IllusionTower, illusion)
        : runs.Find(character) is { } ordinary ? new(DungeonParticipationMode.Ordinary, ordinary.RunId.Value)
        : Residual.GetValueOrDefault(character);

    public bool TryClaim(IReadOnlyList<CharacterId> characters, DungeonParticipationOwner owner) => throw new NotSupportedException();
    public void Release(CharacterId character, DungeonParticipationOwner expected) => throw new NotSupportedException();
    public void ReleaseCommitted(CharacterId character, DungeonParticipationOwner expected) => throw new NotSupportedException();

    private Guid? BossRunOf(CharacterId character) => bossTowers is ICharacterTowerSeats seats ? seats.RunOf(character)
        : sessions?.FindByCharacter(character) is { } session ? bossTowers.Find(session)?.RunId : null;

    private Guid? IllusionRunOf(CharacterId character) => illusionTowers is ICharacterTowerSeats seats ? seats.RunOf(character)
        : sessions?.FindByCharacter(character) is { } session ? illusionTowers.Find(session)?.RunId : null;
}
