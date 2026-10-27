using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.QuestGiveUp;

internal sealed record QuestGiveUpInput(AccountId AccountId, CharacterId CharacterId, ushort QuestId)
    ;

public sealed record GiveUpQuestOutcome(bool GivenUp, ushort QuestId, QuestProgressSnapshot? QuestProgress)
{
    public static GiveUpQuestOutcome Refused(ushort questId) => new(false, questId, null);
}
