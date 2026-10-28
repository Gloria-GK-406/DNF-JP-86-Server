using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Abstractions.Quests;

public interface IQuestMapClearRuleService
{
    int? TargetMapOf(QuestDefinition quest);
    bool Matches(QuestDefinition quest, DungeonMapCleared clear);
}

public sealed record QuestProgressChanged(CharacterId CharacterId) : IDomainEvent;
public sealed record QuestProgressViewChanged(CharacterId CharacterId) : INotification<QuestProgressViewChanged>;
