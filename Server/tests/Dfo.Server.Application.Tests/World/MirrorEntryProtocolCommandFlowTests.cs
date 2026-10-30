using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class MirrorEntryProtocolCommandFlowTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_mirror_decoder_and_command_keep_pair_refusal_and_commit_only_the_valid_entry(bool validPair)
    {
        using var f = TeleportConsumableTests.Fixture(services =>
        {
            services.AddSingleton<IMirrorDungeonRuleService, MirrorRules>();
            services.AddSingleton<IDungeonStructureRuleService, Structures>();
            services.AddSingleton<IDungeonCellFactory, OrdinaryEntryCells>();
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, CurrentView>();
            services.AddSingleton<ICharacterStateProjectionDomainService>(TownMovementProtocolCommandFlowTests.Proxy<ICharacterStateProjectionDomainService>.Create(
                (_, args) => ValueTask.FromResult((SelectCharacterResult)args![0]!)));
        });
        f.SetLevel(60);
        f.Sql("INSERT INTO character_invisible_flags(character_id,slot_index,flag_value) VALUES(1,14395,1)");
        TestGameplay.Run(f.Services, () =>
        {
            f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().CrossInto(f.Session, 21, 2, 865, 208);
            f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().RestoreIfAbsent(f.Session, DungeonEntryIntent.Interactive);
        });
        var body = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(body, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), validPair ? 14299u : 13801u);
        await EffectRequestTransport.SendRaw(f, new(1, 0x02B6, 0, 7), body);
        var run = TestGameplay.Run(f.Services, () => f.Services.DungeonRuns().Find(f.Session.CharacterId));
        if (validPair)
        {
            Assert.NotNull(run); Assert.Equal(new MirrorDungeonRunIdentity(8000, 31, 14299), Assert.IsType<DungeonRunMode.Mirror>(run.Layout.Mode).Plan);
            Assert.Equal((byte)60, run.Layout.FixedMonsterLevel);
            Assert.Equal(1, f.Scalar("SELECT entries FROM character_mirror_daily WHERE character_id=1"));
            Assert.Contains(f.Sender.Outbound.Frames, frame => frame[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x0278
                && BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(15)) == 14299);
            TestGameplay.Run(f.Services, () => Assert.Null(f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Find(f.Session)));
        }
        else
        {
            Assert.Null(run); Assert.Equal(0, f.Scalar("SELECT count(*) FROM character_mirror_daily WHERE character_id=1"));
            var refusal = Assert.IsType<SelectDungeonRefusal>(Assert.Single(f.Sender.Messages)); Assert.True(refusal.Mirror);
            Assert.Equal((ushort)0x02B6, BinaryPrimitives.ReadUInt16LittleEndian(Assert.Single(f.Sender.Outbound.Frames).AsSpan(1)));
            TestGameplay.Run(f.Services, () => Assert.Equal(DungeonEntryIntent.Interactive,
                f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Find(f.Session)));
        }
        Assert.Empty(f.Sender.Outbound.Closes);
    }
    private sealed class MirrorRules : IMirrorDungeonRuleService
    {
        public MirrorDungeonCatalog Catalog { get; } = new(8000, 31, 21, 2, 55, 10, 5, new(5, 3037, 10), [], [new(32, 14299)]) { RequiredQuestIds = [14395] };
        public byte ResolveRunLevel(IReadOnlyCollection<byte> levels) => levels.Max();
        public ValueTask<MirrorDungeonEntryRule?> ResolveAsync(int dungeonId, int questId, CancellationToken ct) =>
            ValueTask.FromResult<MirrorDungeonEntryRule?>(dungeonId == 32 && questId == 14299 ? new(Catalog, DungeonDifficulty.Tier3) : null);
    }
    private sealed class Structures : IDungeonStructureRuleService
    {
        public ValueTask<DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken ct) => ValueTask.FromResult<DungeonDefinition?>(new(id, "mirror-test", 55, true, []));
        public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken ct) =>
            ValueTask.FromResult<DungeonLayout?>(new(id, 0, 1, 1, new(0, 0), new(0, 0), 4403, seed,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(4403) }));
        public ValueTask<DungeonLayout> PrepareLayoutAsync(DungeonLayout layout, CancellationToken ct) => ValueTask.FromResult(layout);
        public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken ct) => ValueTask.FromResult(DungeonMapSelection.Selected(4403));
    }
    private sealed class CurrentView : ISelectedCharacterCurrentViewDomainService
    {
        public async ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken ct) =>
            await Characters.CharacterEntryInventoryTests.CreateLifecycleResultAsync(session.AccountId, session.CharacterId.Value, 0);
    }
}
