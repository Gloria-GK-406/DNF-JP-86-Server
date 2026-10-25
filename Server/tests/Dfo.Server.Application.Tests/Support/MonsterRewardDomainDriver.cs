using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MonsterDeath;
namespace Dfo.Server.Application.Tests.Support;
// Facts for focused domain/algorithm fixtures. This is not registered as a production command,
// and does not prove the protocol request, command pipeline or final whole scope.
internal readonly record struct MonsterRewardIdentity(int Code,int Level,MonsterRank Rank,bool IsNamedMonster=false);
internal sealed record MonsterRewardFacts(AccountId AccountId,CharacterId CharacterId,MonsterRewardIdentity Monster,
    int DungeonId,DungeonDifficulty Difficulty,double DungeonWeight,int EnemyType=HuntEnemyTargetTypes.Monster,
    int PartyMemberCount=1,IReadOnlyList<RolledDrop>? PendingLoot=null,PartyKey PartyKey=default,
    bool IsFinalBossDeath=false,SelectedSessionIdentity? Session=null,DungeonRunId? RunId=null,RoomActorId? Actor=null,
    int? WorldMapId=null) : ICommand<MonsterRewardResult>;
internal static class MonsterRewardDomainDriver
{
    internal static ValueTask<MonsterRewardResult> ApplyAsync(IMonsterKillRewardsDomainService rewards,
        MonsterRewardFacts input,IEffectFootprintRecorder cap,CancellationToken ct)
        => ApplyCoreAsync(rewards,input,cap,ct);
    private static async ValueTask<MonsterRewardResult> ApplyCoreAsync(IMonsterKillRewardsDomainService rewards,MonsterRewardFacts input,IEffectFootprintRecorder cap,CancellationToken ct)

        {
            var result=await rewards.ApplyAsync(input.Session,input.AccountId,input.CharacterId,
                new(input.Monster.Code,input.Monster.Level,input.Monster.Rank,input.EnemyType==HuntEnemyTargetTypes.Monster,input.Monster.IsNamedMonster),
                input.DungeonId,input.Difficulty,input.DungeonWeight,input.PartyMemberCount,input.PartyKey,input.IsFinalBossDeath,
                input.RunId,input.Actor,input.WorldMapId,input.PendingLoot,cap,ct);
            return new MonsterRewardResult(result.Award,result.QuestChanges.Select(x=>new QuestAdvanceFact(x.QuestId,x.Before,x.After)).ToArray(),
                result.QuestItems,result.QuestProgress,result.AccountMaterials);
        }
}

internal sealed record QuestAdvanceFact(int QuestId,uint Before,uint After);
internal sealed record MonsterRewardResult(ExperienceAward? Award,IReadOnlyList<QuestAdvanceFact> QuestAdvances,
    Dfo.Server.Data.Game.CommittedInventoryUpdateState? QuestItems=null,QuestProgressSnapshot? QuestProgress=null,
    IReadOnlyList<Dfo.Server.Data.Inventory.InventoryRuntimeValueState>? AccountMaterials=null);
