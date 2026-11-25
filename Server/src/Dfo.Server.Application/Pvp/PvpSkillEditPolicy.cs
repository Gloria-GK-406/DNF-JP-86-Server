using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpSkillEditPolicy(
    IChannelEntryDomainService channels,
    IPvpRoomQueryDomainService rooms,
    IOnlineCharacterRuntimeDomainService onlineCharacters) : ICharacterSkillGroupEditPolicy
{
    public SkillGroup Group => SkillGroup.Duel;

    public async ValueTask<CharacterSkillLearningRejection> RefuseEditAsync(
        SelectedSessionIdentity session, CharacterSkillGroupEditOperation operation, CancellationToken cancellationToken)
    {
        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken).ConfigureAwait(false);
        // The A21 matchmaking lobby has confirmed learning; slot moves and build switches are verified in traditional lobbies.
        var supported = channel.Kind is ChannelEntryKind.TraditionalDuel
            || (channel.SupportsMatchmaking && operation is CharacterSkillGroupEditOperation.Learn);
        if (!supported)
            return CharacterSkillLearningRejection.UnsupportedScenario;
        var room = rooms.FindSeated(session.CharacterId);
        var mayEdit = room is null
            ? onlineCharacters.StandingOf(session) is not null
            : room.Phase == PvpRoomPhase.Waiting
                && room.Admission is null
                && room.ListenerPort == session.ListenerPort
                && room.Members.Any(member => member.Member.Session == session);
        return mayEdit ? CharacterSkillLearningRejection.None : CharacterSkillLearningRejection.UnsupportedScenario;
    }
}
