using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public sealed record GuildDungeonProgressChanged(CharacterId Character, GuildDungeonAdmission Admission) : IDomainEvent
{
    public bool RepeatBonusAdvanced { get; init; }
    public bool RefreshGuildInfo { get; init; }
}
public sealed record GuildDungeonProgressNotification(GuildDungeonView View) : INotification<GuildDungeonProgressNotification>
{
    public bool RefreshGuildInfo { get; init; }
}
public sealed record GuildDungeonRepeatBonusNotification(int Progress) : INotification<GuildDungeonRepeatBonusNotification>;
