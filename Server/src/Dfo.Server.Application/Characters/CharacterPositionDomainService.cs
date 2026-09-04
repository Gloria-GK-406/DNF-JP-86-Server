using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Keeps where each character being played is standing, in memory, for the server's lifetime.
/// </summary>
/// <remarks>
/// The same shape as the presence, expert-store and duelling-lobby services: a private dictionary
/// behind questions and reports written in the domain's own words. The dictionary is an
/// implementation detail -- nothing here hands a position out to be edited and handed back, which
/// is exactly what moving a character used to mean.
/// <para>
/// Keyed by character rather than by account, because that is who is standing somewhere and who
/// the question names. The session is carried alongside so that a displaced generation can neither
/// move its replacement nor forget it.
/// </para>
/// <para>
/// One lock over the whole dictionary, as the runtime directory this takes the position out of
/// also has. A report reads the position it moves, so read and store have to be one step: two
/// reports arriving together would otherwise each apply to what they read, and the second would
/// store a position the first had already moved away from.
/// </para>
/// </remarks>
internal sealed class CharacterPositionDomainService : ICharacterPositionDomainService
{
    private readonly object sync = new();
    private readonly Dictionary<CharacterId, Standing> byCharacter = [];

    public void Place(SelectedSessionIdentity session, CharacterPositionState position)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);
        lock (this.sync)
            this.byCharacter[session.CharacterId] = new Standing(session, position);
    }

    public CharacterPositionState? Find(CharacterId characterId)
    {
        lock (this.sync)
            return this.byCharacter.TryGetValue(characterId, out var standing)
                ? standing.Position
                : null;
    }

    /// <remarks>
    /// The facing is the one the character already had: the crossing report does not carry one.
    /// </remarks>
    public CharacterPositionState CrossInto(
        SelectedSessionIdentity session, byte townId, byte areaId, short x, short y) =>
        this.Move(
            session,
            current => current with { TownId = townId, AreaId = areaId, X = x, Y = y });

    public CharacterPositionState StepTo(
        SelectedSessionIdentity session, short x, short y, byte direction) =>
        this.Move(
            session,
            current => current with { X = x, Y = y, Direction = direction });

    public bool ForgetExact(SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (this.sync)
            return this.byCharacter.TryGetValue(session.CharacterId, out var standing)
                && standing.Session == session
                && this.byCharacter.Remove(session.CharacterId);
    }

    /// <summary>Applies one report to the position this session put there, or refuses.</summary>
    private CharacterPositionState Move(
        SelectedSessionIdentity session, Func<CharacterPositionState, CharacterPositionState> report)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (this.sync)
        {
            if (!this.byCharacter.TryGetValue(session.CharacterId, out var standing)
                || standing.Session != session)
            {
                throw new InvalidOperationException(
                    "The session reporting a movement is not the one standing there.");
            }

            var moved = report(standing.Position);
            this.byCharacter[session.CharacterId] = standing with { Position = moved };
            return moved;
        }
    }

    /// <summary>One character's live position and the session generation that owns it.</summary>
    private sealed record Standing(SelectedSessionIdentity Session, CharacterPositionState Position);
}
