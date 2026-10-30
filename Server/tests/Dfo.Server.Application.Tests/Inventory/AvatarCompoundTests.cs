using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.AvatarCompound;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class AvatarCompoundTests
{
    internal sealed class Content : IAvatarCompoundDefinitionSource
    {
        public int Rate { get; set; }
        public int Grade1 { get; set; } = 2;
        public int Grade2 { get; set; } = 2;
        public string Part2 { get; set; } = "hat avatar";
        public ValueTask<AvatarCompoundDefinition?> ForJobAsync(byte job, CancellationToken ct) =>
            ValueTask.FromResult<AvatarCompoundDefinition?>(new(1, 2, 200, [new(0, 21, 1), new(1, 899, 1)],
                [new("hat avatar", Rate, Rate is 0 or 10000 ? 0 : 325, [new(42601, 1)], [new(39088, 1)])]));
        public ValueTask<AvatarCompoundItemDefinition?> ItemAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<AvatarCompoundItemDefinition?>(id switch
            {
                39086 => new(id, "hat avatar", Grade1, [0, 1], true, false),
                39087 => new(id, Part2, Grade2, [0, 1], true, false),
                39088 => new(id, "hat avatar", 2, [0, 1], true, false),
                42601 => new(id, "hat avatar", 3, [0, 1], true, false),
                _ => null,
            });
    }
    internal static ShopFixture Fixture(Content? content = null, Action<IServiceCollection>? configure = null, bool archive = true)
    {
        if (archive) Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH unavailable.");
        var f = new ShopFixture(s =>
        {
            if (archive)
            {
                Dfo.Server.Application.Tests.Support.ShopFixtureContent.RegisterArchive(s);
                s.AddSingleton<IItemPlacementDefinitionSource>(sp => new PvfItemPlacementDefinitionSource(sp.GetRequiredService<PvfArchiveProvider>(), new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService()));
            }
            else
            {
                s.AddSingleton<IItemPlacementDefinitionSource, CompoundPlacement>();
                s.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((21,
                    new(true, 0, 0, 1, ItemJobRestriction.Any, ItemPlaceRestriction.Anywhere))));
            }
            if (content is not null) s.AddSingleton<IAvatarCompoundDefinitionSource>(content);
            configure?.Invoke(s);
        });
        f.Sql("UPDATE characters SET job=0"); f.SetLevel(80);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(f.Session.CharacterId,
            new PersistentItemContainer(ContainerId.ForCharacter(f.Session.CharacterId, InventoryContainerType.Avatar),
                new(InventoryContainerType.Avatar, 0), f.Services.GetRequiredService<IItemDomainStore>(),
                f.Services.GetRequiredService<IDomainTransactionAccessor>(), f.Services.GetRequiredService<IItemPlacementRuleService>(),
                f.Services.GetRequiredService<IItemSlotRuleService>())));
        return f;
    }
    // Wire/transaction tests declare only the two source avatars, two possible outputs and
    // material they exercise. Archive characterization keeps the real placement source above.
    private sealed class CompoundPlacement : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<ItemPlacement?>(id switch
            {
                39086 or 39087 or 39088 or 42601 => new(8, 1, new(BusinessInventorySpace.Avatar, 0, 209)) { WorldClass = ItemWorldClass.Avatar },
                21 => new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 55)),
                _ => null,
            });
    }
    internal static async Task Seed(ShopFixture f, int box = 21, int count = 2)
    {
        await f.Seed(65, box, count);
        await f.SeedCore(0, StackableItemCore.Create(8, 39086, 12345), InventoryContainerType.Avatar);
        await f.SeedCore(1, StackableItemCore.Create(8, 39087, 54321), InventoryContainerType.Avatar);
    }
    private static AvatarCompoundInput Input(int target = 39088, byte category = 0, ushort ability = 0) =>
        new(new(BusinessInventorySpace.Consumable, 0), new(BusinessInventorySpace.Avatar, 0), 39086,
            new(BusinessInventorySpace.Avatar, 1), 39087, target, ability, target == 1 ? (byte)1 : (byte)0, category);
    private static Task<AvatarCompoundResult> Roll(ShopFixture f, AvatarCompoundInput? input = null) =>
        AvatarCompoundDomainTestDriver.Compound(f, f.Session, input ?? Input());
    private static ValueTask Send(ShopFixture f, ushort sequence = 7, int target = 39088, byte category = 0) =>
        EffectRequestTransport.Send(f, new AvatarCompoundRequest(new(1, 0x63, 0, sequence), 65, 0, 39086, 1, 39087,
            target, 0, target == 1 ? (byte)1 : (byte)0, category));
    private static IEnumerable<byte[]> Bodies(ShopFixture f)
    {
        foreach (var message in f.Sender.Messages)
        {
            var registration = f.Services.GetServices<ServerMessageEncoderRegistration>().Single(x => x.MessageType == message.GetType());
            foreach (var frame in ((IServerMessageEncoder)f.Services.GetRequiredService(registration.EncoderType)).Encode(message))
                yield return frame[15..];
        }
    }
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transaction_consumes_two_inputs_and_box_and_creates_independent_outputs(bool rare)
    {
        using var f = Fixture(new() { Rate = rare ? 10000 : 0 }); await Seed(f);
        var oldUid = f.Scalar("SELECT MAX(core_uid) FROM item_slots WHERE list_type=1");
        var result = await Roll(f); Assert.Equal(AvatarCompoundRejection.None, result.Rejection);
        Assert.Equal(rare ? 2 : 1, result.Receipt!.Outputs.Length);
        Assert.Equal(0, f.Count(39086)); Assert.Equal(0, f.Count(39087)); Assert.Equal(1, f.Count(21));
        Assert.Equal(1, f.Count(39088)); Assert.Equal(rare ? 1 : 0, f.Count(42601));
        Assert.All(result.Receipt.Outputs, x => { Assert.True(x.Item.InstanceValue > 0); Assert.Equal(0, x.Item.Attribute);
            Assert.Equal(new byte[30], x.Item.AvatarDetail!.JewelSocket); Assert.Equal(0, x.Item.AvatarDetail.ExpireDate); });
        Assert.True(f.Scalar("SELECT MIN(core_uid) FROM item_slots WHERE list_type=1") > oldUid);
        Assert.Empty(f.Sender.Messages);
    }
    [SkippableTheory]
    [InlineData(false, 89)]
    [InlineData(true, 131)]
    public async Task Wire_success_contains_consumed_quantities_and_fixed_two_conditional_records(bool rare, int length)
    {
        using var f = Fixture(new() { Rate = rare ? 10000 : 0 }); await Seed(f);
        await Send(f); Assert.Single(f.Sender.Messages); var b = Assert.Single(Bodies(f));
        Assert.Equal(length, b.Length); Assert.Equal(1, b[0]); Assert.Equal(3, b[1]);
        Assert.Equal(1, b[2]); Assert.Equal(1, b[9]); Assert.Equal(0, b[16]);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(5)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(19)));
        Assert.Equal(39088, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(25)));
        Assert.Equal(30, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(35)));
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(69)));
        Assert.Equal(rare ? 42601 : 0, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(79)));
    }
    [SkippableTheory]
    [InlineData("material")]
    [InlineData("template")]
    [InlineData("slot")]
    [InlineData("target")]
    [InlineData("ability")]
    [InlineData("category")]
    [InlineData("part")]
    [InlineData("grade")]
    [InlineData("lock")]
    [InlineData("expire")]
    public async Task Rejections_leave_all_inputs_unchanged(string reason)
    {
        var content = new Content(); using var f = Fixture(content); await Seed(f);
        var input = Input();
        if (reason == "material") await f.Seed(65, 899, 2);
        if (reason == "template") input = input with { FirstItemId = 39087 };
        if (reason == "slot") input = input with { Second = input.First };
        if (reason == "target") input = input with { TargetItemId = 42601 };
        if (reason == "ability") input = input with { Ability = 99 };
        if (reason == "category") input = input with { Category = 2 };
        if (reason == "part") content.Part2 = "coat avatar";
        if (reason == "grade") content.Grade1 = 3;
        if (reason is "lock" or "expire") await f.SeedCore(0, StackableItemCore.Create(8, 39086, 12345) with
            { EquipmentLockId = reason == "lock" ? (byte)1 : (byte)0, ExpireTime = reason == "expire" ? 123 : 0 }, InventoryContainerType.Avatar);
        var before = f.StoredCore(0, InventoryContainerType.Avatar);
        var result = await Roll(f, input); Assert.Null(result.Receipt); Assert.NotEqual(AvatarCompoundRejection.None, result.Rejection);
        Assert.Equal(before, f.StoredCore(0, InventoryContainerType.Avatar)); Assert.Equal(1, f.Count(39087));
        Assert.Equal(0, f.Count(39088)); Assert.Equal(2, f.Count(reason == "material" ? 899 : 21));
    }
    [SkippableFact]
    public async Task Failure_after_creating_results_rolls_back_every_item_and_uid()
    {
        using var f = Fixture(new() { Rate = 10000 }); await Seed(f);
        var a = f.StoredCore(0, InventoryContainerType.Avatar);
        f.Sql("CREATE TRIGGER compound_failure BEFORE DELETE ON item_slots BEGIN SELECT RAISE(ABORT,'compound-failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => Roll(f));
        Assert.Equal(a, f.StoredCore(0, InventoryContainerType.Avatar)); Assert.Equal(2, f.Count(21));
        Assert.Equal(0, f.Count(39088)); Assert.Equal(0, f.Count(42601));
    }
    [SkippableFact]
    public async Task Concurrent_compounds_cannot_consume_the_same_pair_twice()
    {
        using var f = Fixture(new()); await Seed(f);
        var results = await Task.WhenAll(Roll(f), Roll(f)); Assert.Single(results, x => x.Receipt is not null);
        Assert.Equal(1, f.Count(21)); Assert.Equal(1, f.Count(39088));
    }
    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Authoritative_job_and_level_refuse_unusable_avatars_without_spending(bool job)
    {
        using var f = Fixture(); await Seed(f);
        if (job) f.Sql("UPDATE characters SET job=1"); else f.SetLevel(1);
        var result = await Roll(f); Assert.Null(result.Receipt);
        Assert.Equal(1, f.Count(39086)); Assert.Equal(1, f.Count(39087)); Assert.Equal(2, f.Count(21));
    }
    [SkippableFact]
    public async Task Duplicate_wire_request_restores_UI_before_current_inventory_without_repeating_delta()
    {
        using var f = Fixture(new() { Rate = 10000 }); await Seed(f);
        await Send(f); f.Sender.Messages.Clear(); f.Sender.Batches.Clear(); await Send(f);
        Assert.True(Assert.IsType<AvatarCompoundResponse>(f.Sender.Messages[0]).Recovery);
        var recovery = Bodies(f).First(); Assert.Equal(26, recovery.Length); Assert.Equal(1, recovery[0]); Assert.Equal(0, recovery[1]);
        Assert.All(new[] { 4, 16 }, offset => Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(recovery.AsSpan(offset))));
        Assert.Contains(f.Sender.Messages.Skip(1), x => x is not AvatarCompoundResponse);
        Assert.Equal(1, f.Count(21)); Assert.Equal(1, f.Count(39088)); Assert.Equal(1, f.Count(42601));
    }
    [SkippableFact]
    public async Task Real_PVF_job_pool_materials_options_and_golden_random_mode_are_usable()
    {
        using var f = Fixture(); var source = f.Services.GetRequiredService<IAvatarCompoundDefinitionSource>();
        var definition = await source.ForJobAsync(0, default); Assert.NotNull(definition); Assert.Equal(8, definition.Parts.Length);
        Assert.Equal(new[] { 21, 899 }, definition.Materials.Select(x => x.ItemId)); Assert.Equal(200, definition.GoldenBonus);
        var hat = Assert.Single(definition.Parts, x => x.EquipmentType == "hat avatar");
        Assert.Equal((900, 325), (hat.RareRate, hat.UpperRate)); Assert.Contains(hat.Advanced, x => x.ItemId == 39088);
        var target = await source.ItemAsync(39088, default); Assert.NotNull(target); Assert.Contains(0, target.Abilities);
        Assert.True(target.Jobs.IsValid); Assert.Equal(30, target.MinimumLevel);
        await Seed(f, 899); var result = await Roll(f, Input(1, 1)); Assert.NotNull(result.Receipt);
        Assert.Equal(1425, result.Receipt.RareRate); Assert.Contains(hat.Advanced, x => x.ItemId == result.Receipt.Outputs[0].Item.ItemId);
    }
    [Theory]
    [InlineData(1, 1, 900)]
    [InlineData(1, 2, 1062)]
    [InlineData(2, 2, 1225)]
    public async Task Probability_policy_has_three_tiers_and_golden_additive_bonus(int a, int b, int expected)
    {
        var content = new Content { Grade1 = a, Grade2 = b, Rate = 900 };
        var rule = new AvatarCompoundRuleService(content);
        var first = new ItemSnapshot { ItemId = 39086 }; var second = new ItemSnapshot { ItemId = 39087 };
        var normal = await rule.PlanAsync(0, 80, Input(), 21, first, second, 42, default);
        var gold = await rule.PlanAsync(0, 80, Input(category: 1), 899, first, second, 42, default);
        Assert.Equal(expected, normal.RareRate); Assert.Equal(expected + 200, gold.RareRate);
    }
    [Fact]
    public void Generic_full_snapshot_does_not_trigger_compound_UI_and_UI_recovery_needs_inventory()
    {
        Assert.False(ReplayProjectionNeeds.All.HasFlag(ReplayProjectionNeeds.AvatarCompound));
        Assert.Throws<InvalidDataException>(() => new ReplayProjectionRequest(ReplayProjectionNeeds.AvatarCompound, []).Validate());
        new ReplayProjectionRequest(ReplayProjectionNeeds.AvatarCompound | ReplayProjectionNeeds.Inventory, []).Validate();
    }
    [SkippableTheory]
    [InlineData(21)]
    [InlineData(23)]
    [InlineData(26)]
    public void Decoder_rejects_short_extra_and_bad_checksum(int length)
    {
        using var f = Fixture(new()); var r = f.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == 0x63);
        var decoded = Assert.IsType<AvatarCompoundRequest>(((IGameMessageDecoder)f.Services.GetRequiredService(r.DecoderType)).Decode(GameMessageMetadata.Empty, new byte[length]));
        Assert.False(decoded.Valid);
    }
}
