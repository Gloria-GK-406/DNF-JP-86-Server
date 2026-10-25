using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.QuestCompletion;

// Durable settlement regressions live in Application/Quests/OrdinaryQuestEffectTests.
// This slice test checks the boundary's receipt projection rather than recreating settlement.
public sealed class FinishQuestCommandHandlerTests
{
    [Fact]
    public async Task Command_projects_every_basic_addition_and_actual_gold_with_earned_experience()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var placement = new Dfo.Server.Abstractions.Content.ItemPlacement(2, 5, new(BusinessInventorySpace.Consumable, 0, 1));
            var grant = new GrantBasicCharacterItemEffect(2000, 3, placement);
            var applied = new ApplyEffectsResult([
                new(grant, new BasicCharacterItemChange([
                    new(new(BusinessInventorySpace.QuickSlot, ContainerId.ForCharacter(new(1), InventoryContainerType.Main)), new(BusinessInventorySpace.QuickSlot,0),1),
                    new(new(BusinessInventorySpace.Consumable, ContainerId.ForCharacter(new(1), InventoryContainerType.Main)), new(BusinessInventorySpace.Consumable,0),2)])),
                new(new SetQuestRewardGoldEffect(10,15), new QuestRewardGoldChange(10,15))]);
            var receipt = new QuestCompletionReceipt(true,2001,"meet npc",new(new(Base:100),86,700,false),applied,null,null);
            var output = await Run(new Completion(receipt));
            Assert.True(output.Finished);
            Assert.Equal(100u,output.Experience);
            Assert.Equal(700u,output.NewExperience);
            Assert.Equal(new[]{new FinishedQuestItem(3,2000,1),new FinishedQuestItem(65,2000,2),new FinishedQuestItem(0,0,5)},output.Items);
        });
    }
    [Fact]
    public async Task Command_uses_the_settlement_receipt_mode_without_reclassifying_the_quest()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var receipt = new QuestCompletionReceipt(true, 2001, "condition under clear",
                Dfo.Server.Abstractions.World.ExperienceAward.Nothing(86, 700), new([]), null, null) { MirrorCompletion = true };
            var result = await Run(new Completion(receipt));
            Assert.True(result.Finished);
            Assert.True(result.MirrorCompletion);
            Assert.Empty(result.Items);
        });
    }

    [Fact]
    public async Task Refusal_keeps_its_readonly_effective_progress_projection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var projection = new QuestProgressSnapshot([new(0,2001,1)]);
            var result = await Run(new Completion(new(false,2001,null,null,null,projection,null)));
            Assert.False(result.Finished);
            Assert.Same(projection,result.QuestProgress);
        });
    }
    [Fact]
    public async Task Late_fault_is_wrapped_with_its_cause_for_transaction_rollback_and_uncertainty_audit()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var cause = new IOException("late write");
            var error = await Assert.ThrowsAsync<QuestFinishRejectedException>(async()=>await Run(new Completion(null,cause)));
            Assert.Same(cause,error.InnerException);
        });
    }
    [Fact]
    public async Task Recursive_uncertainty_is_not_wrapped_as_business_refusal()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var unknown = new Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException(
                Dfo.Server.Abstractions.Transactions.TransactionUncertaintyReason.RollbackFailure,"unknown",new IOException("rollback"));
            var wrapped = new AggregateException(new IOException("other"),new InvalidOperationException("wrapper",unknown));
            var result = await Assert.ThrowsAsync<AggregateException>(async () => await Run(new Completion(null,wrapped)));
            Assert.Same(wrapped,result);
        });
    }
    private static ValueTask<QuestFinishOutcome> Run(Completion completion) => TestEffectFootprints.Run(new EffectFootprintOwner(new(1),new(1),null),footprint =>
        QuestOperationDriver.Invoke<QuestFinishOutcome>("QuestCompletion","FinishQuest","QuestCompletionInput","CompleteAsync",[completion],
            [new AccountId(1),new CharacterId(1),(ushort)2001,ushort.MaxValue,(ushort)1,(uint)0,null],
            new(Guid.Empty,typeof(object),typeof(object),CommandExecutionKind.Transactional),footprint,default));
    private sealed class Completion(QuestCompletionReceipt? receipt,Exception? error=null):IQuestCompletionSettlementDomainService
    {
        public ValueTask<QuestCompletionReceipt> FinishAsync(AccountId account,CharacterId id,ushort quest,ushort selection,ushort count,DateTimeOffset at,Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken ct,Dfo.Server.Data.World.SelectedSessionIdentity? session=null)
        {
            if(error is not null) throw error;
            Assert.Equal(ushort.MaxValue,selection); Assert.Equal(1,count);
            return ValueTask.FromResult(receipt!);
        }
    }
}
