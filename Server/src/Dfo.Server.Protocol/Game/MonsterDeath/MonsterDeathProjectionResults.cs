using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.MonsterDeath;
public sealed record ObjectQuestReward(SelectedSessionIdentity Session, MonsterKillEffects Effects);
public sealed record ObjectDestructionResult(IReadOnlyList<ObjectQuestReward>? Rewards);
