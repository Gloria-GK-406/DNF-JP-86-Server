using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.World;
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
        SelectedSessionIdentity session, SkillBuildId build, CancellationToken cancellationToken)
    {
        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken).ConfigureAwait(false);
        // The A21 samples establish editing on page zero in the environment-24 channel.
        if (!channel.IsPvp || channel.LoginEnvironment != 24)
            return CharacterSkillLearningRejection.UnsupportedScenario;
        if (build.Value != 0)
            return CharacterSkillLearningRejection.PageUnavailable;

        var room = rooms.FindSeated(session.CharacterId);
        var mayEdit = room is null
            ? onlineCharacters.StandingOf(session) is not null
            : room.Phase == PvpRoomPhase.Waiting
                && room.ListenerPort == session.ListenerPort
                && room.Members.Any(member => member.Member.Session == session);
        return mayEdit ? CharacterSkillLearningRejection.None : CharacterSkillLearningRejection.UnsupportedScenario;
    }
}
