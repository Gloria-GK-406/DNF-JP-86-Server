using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Quests;

public interface IQuestMapClearRuleService
{
    int? TargetMapOf(QuestDefinition quest);
}

public sealed record QuestProgressChanged(CharacterId CharacterId) : IDomainEvent;
public sealed record QuestProgressViewChanged(CharacterId CharacterId) : INotification<QuestProgressViewChanged>;
