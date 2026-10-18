using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MonsterDeath;

namespace Dfo.Server.Protocol.Game;

internal static class GameExperienceDelivery
{
    public static IGameServerMessage Notification(ExperienceAward award, CharacterSkillProtocolMirrorState points) =>
        new ExperienceNotification(award.Level, award.NotificationTotal, points,
            AccountHonorLevel.Initial, ExperienceBonusLines.Of(award));
}
