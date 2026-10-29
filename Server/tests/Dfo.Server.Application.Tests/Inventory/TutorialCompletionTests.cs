using Dfo.Server.Data.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Abstractions.World;
using Microsoft.Data.Sqlite;
using Dfo.Server.Application.Commands;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Schema;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class TutorialCompletionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Completed_level_one_tutorial_is_skipped_on_fresh_selection_and_other_characters_are_unchanged(byte reward)
    {
        using var host = new ShopFixture(services => services.AddSingleton<ITutorialRewardDefinitionSource>(new Rewards()));
        host.Sql($"UPDATE characters SET level=1,exp={host.ExperienceForLevel(1)}");
        var before = await Entry(host, 0);
        Assert.Equal(0, before[^25]);
        await Report(host, 30, 0);
        Assert.Equal(before, await Entry(host, 0));
        await Report(host, 31, reward);
        var after = await Entry(host, 0);
        Assert.Equal(1, after[^25]);
        Assert.Equal(0x4e, after[^41]);
        Assert.Equal(before.Length + 1, after.Length);
        Assert.False(host.Services.GetRequiredService<IDungeonVisitDomainService>().DefersSelectionState(1, true));
        Assert.Equal(1, host.Scalar("SELECT level FROM characters WHERE character_id=1"));
        Assert.Equal(0, (await Entry(host, 1))[^25]);
        await Report(host, 31, reward);
        Assert.Equal(after, await Entry(host, 0));
    }

    [Fact]
    public async Task Reward_flag_is_honored_and_completion_reward_is_granted_only_once()
    {
        using var host = new ShopFixture(services => services.AddSingleton<ITutorialRewardDefinitionSource>(new Rewards(true)));
        await Report(host, 38, 0);
        Assert.Equal(0, host.Count(8474));
        await Report(host, 31, 1);
        Assert.Equal(1, host.Count(8474));
        await Report(host, 31, 1);
        Assert.Equal(1, host.Count(8474));
        Assert.Empty(host.Sender.Messages.OfType<TutorialFlagAcknowledgement>().Last().Granted);
    }

    [Fact]
    public async Task Failed_reward_write_rolls_back_completion_and_sends_no_success()
    {
        using var host = new ShopFixture(services => services.AddSingleton<ITutorialRewardDefinitionSource>(new Rewards(true)));
        host.Sql("CREATE TRIGGER reject_tutorial_reward BEFORE INSERT ON item_slots BEGIN SELECT RAISE(ABORT, 'forced reward failure'); END");
        await Assert.ThrowsAsync<SqliteException>(async () => await Report(host, 31, 1));
        Assert.Equal(0, (await Entry(host, 0))[^25]);
        Assert.Equal(0, host.Count(8474));
        Assert.Empty(host.Sender.Messages);
        host.Sql("DROP TRIGGER reject_tutorial_reward");
        await Report(host, 31, 1);
        Assert.Equal(1, (await Entry(host, 0))[^25]);
        Assert.Equal(1, host.Count(8474));
    }

    [Fact]
    public async Task Completion_cannot_modify_a_character_owned_by_another_account()
    {
        using var host = new ShopFixture();
        var result = await TestGameplay.RunAsync(host.Services, async () => {
            await using var tx=((Dfo.Server.Application.Transactions.DomainTransactionScope)host.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>()).Begin();
            var value=await GameTaggedIdReportCommandHandler.GrantAsync(host.Services.GetRequiredService<Dfo.Server.Abstractions.Inventory.ICharacterContainerDomainService>(),host.Services.GetRequiredService<Dfo.Server.Abstractions.Inventory.IItemDeliveryDomainService>(),host.Services.GetRequiredService<ITutorialCompletionDomainService>(),new(1),[new(8474,1)],new(2),true,default);
            await tx.CommitAsync(default);return value;
        });
        Assert.Empty(result.Placed);
        Assert.Equal(0, (await Entry(host, 0))[^25]);
        Assert.Equal(0, host.Count(8474));
    }

    [Fact]
    public async Task Existing_database_gains_the_completion_column_without_changing_characters()
    {
        using var host = new ShopFixture();
        host.Sql("ALTER TABLE characters DROP COLUMN tutorial_completed");
        var database = host.Services.GetRequiredService<SqliteDatabase>();
        CharacterDatabaseSchema.Ensure(database.ConnectionString);
        CharacterDatabaseSchema.Ensure(database.ConnectionString);
        Assert.Equal(2, host.Scalar("SELECT COUNT(*) FROM characters WHERE level=1 AND tutorial_completed=0"));
        Assert.Equal(100000, host.Gold);
        Assert.Equal(0, (await Entry(host, 0))[^25]);
    }

    private static ValueTask Report(ShopFixture host,uint flag,byte reward) =>
        EffectRequestTransport.SendRaw(host,new(1,0x8F,0,7),[0,..BitConverter.GetBytes(flag),reward]);

    private static async Task<byte[]> Entry(ShopFixture host, ushort slot)
    {
        var character = await host.Services.GetRequiredService<ICharacterRosterDomainService>()
            .LoadByRosterPositionAsync(new(1), slot, default);
        var account = new AccountEntryState(new(1), new([], [], [], 0, null), new(0, 0, 0, 0), new([]), new(0, 0, new([]), []), new([], [], [], 0));
        var codec = typeof(TaggedIdReportHandler).Assembly.GetType(
            "Dfo.Server.Protocol.Game.StateSynchronization.StateSynchronizationPayloadCodec")!;
        return (byte[])codec.GetMethod("EncodeEntryAccepted")!.Invoke(null,
            [character, account, new Dfo.Server.Data.Characters.CharacterQuestEntryState([], [], [], []), (byte)1, host.Clock.GetUtcNow()])!;
    }

    private sealed class Rewards(bool grant = false) : ITutorialRewardDefinitionSource
    {
        public ValueTask<IReadOnlyList<TutorialRewardItem>> GetAsync(uint tutorialFlag, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<TutorialRewardItem>>(grant ? [new(8474, 1)] : []);
    }
}
