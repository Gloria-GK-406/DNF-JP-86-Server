using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Tests.Quests;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ExpertJobCraftProtocolTests
{
    private const ushort Craft = 0x00EE;
    private const int Recipe = 90001;
    private const int NextRecipe = 90002;
    private const int Material = 2000;
    private const int Product = 1000;

    [SkippableFact]
    public async Task Lowest_doll_extractor_rejects_unpurified_equipment_without_changing_inventory()
    {
        const ushort extract = 0x01A0;
        const int equipment = 100170123;
        const int lowestExtractor = 2600474;
        const int targetSlot = 9;
        const int extractorSlot = 87;

        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var fixture = new ShopFixture(services =>
            services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>());
        fixture.Sql($"INSERT INTO character_expert_job(character_id,expert_job_type) VALUES(1,{(byte)ExpertJobType.DollController})");
        await fixture.SeedCore(targetSlot, StackableItemCore.Create(1, equipment, 1234) with
        {
            Durability = 30,
            AmplifyType = 0x80,
        });
        await fixture.Seed(extractorSlot, lowestExtractor, 1);
        var original = fixture.StoredCore(targetSlot);

        byte[] request = [(byte)ExpertJobType.DollController, extractorSlot, 0, 0, targetSlot, 0];
        await EffectRequestTransport.SendRaw(fixture, new(1, extract, 0, EffectRequestTransport.FreshSequence(fixture)), request);

        var ack = fixture.Sender.Outbound.Frames.Single(frame =>
            frame[0] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == extract);
        Assert.Equal(new byte[] { 0, 175 }, ack[15..]);
        Assert.Equal(original, fixture.StoredCore(targetSlot));
        Assert.Equal(1, fixture.Count(lowestExtractor));
        Assert.Equal(0, fixture.Count(2610044));
        Assert.Equal(0, fixture.Scalar("SELECT expert_job_exp FROM character_expert_job WHERE character_id=1"));
    }

    [Theory]
    [InlineData(ExpertJobType.Alchemist, 193)]
    [InlineData(ExpertJobType.DollController, 192)]
    public async Task Batch_craft_spends_each_attempt_and_persists_rewards_experience_and_new_recipe(ExpertJobType type, int skill)
    {
        using var fixture = new ShopFixture(questProtocol: true, configure: services =>
        {
            services.AddSingleton<IExpertJobDefinitionSource>(new CraftContent(type, skill));
            services.AddSingleton<IQuestContentRuleService>(new OrdinaryQuestEffectTests.Rules());
        });
        await fixture.Seed(65, Material, 5);
        var body = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(body, Recipe);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), 2);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(6), -1);

        await Send(fixture, body);
        Assert.Equal(0, LastAcknowledgement(fixture)[15]);
        Assert.Equal(5, fixture.Count(Material));
        Assert.Equal(0, fixture.Count(Product));
        Assert.Equal(100000, fixture.Gold);

        fixture.Sql($"""
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0);
            INSERT INTO character_pvp_record(character_id) VALUES(1);
            INSERT INTO character_pvp_skill_state(character_id) VALUES(1);
            INSERT INTO character_settings(character_id) VALUES(1);
            INSERT INTO character_expert_job(character_id,expert_job_type) VALUES(1,{(byte)type});
            INSERT INTO character_expert_job_recipes(character_id,recipe_id) VALUES(1,{Recipe});
            INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level)
            VALUES(1,'pve',0,0,{skill},1);
            """);
        await Send(fixture, body);

        Assert.Equal(1, fixture.Count(Material));
        Assert.Equal(6, fixture.Count(Product));
        Assert.Equal(99950, fixture.Gold);
        var profession = fixture.Services.GetRequiredService<IExpertJobDomainService>().Get(fixture.Session.CharacterId);
        var state = await profession.GetStateAsync(default);
        Assert.Equal(6u, state.Experience);
        Assert.Equal(new[] { Recipe, NextRecipe }, state.LearnedRecipeIds);
        Assert.Equal(1, LastAcknowledgement(fixture)[15]);
    }

    private static ValueTask Send(ShopFixture fixture, byte[] body) =>
        EffectRequestTransport.SendRaw(fixture, new(1, Craft, 0, EffectRequestTransport.FreshSequence(fixture)), body);

    private static byte[] LastAcknowledgement(ShopFixture fixture) => fixture.Sender.Outbound.Frames.Last(frame =>
        frame[0] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == Craft);

    private sealed class CraftContent(ExpertJobType type, int skill) : IExpertJobDefinitionSource
    {
        private readonly ExpertJobDefinition definition = new(type, [5, 20], [1000], new HashSet<int>(),
            new HashSet<int>(), new Dictionary<int, byte> { [skill] = 1 }, 0, 0,
            new Dictionary<int, int> { [2] = NextRecipe }, new Dictionary<byte, int>())
        {
            Production = new(
                new Dictionary<int, ExpertJobRecipeDefinition> { [Recipe] = new(1, skill, 1, [new(Material, 2)], new(Product, 3), 25, new(3, 3)) },
                [new(0, 100, new(3, 3))],
                new Dictionary<int, ExpertJobExtractorDefinition>(),
                new Dictionary<(int, int, int), ExpertJobExtractionRule>(),
                [], [], null),
        };
        public ValueTask<ExpertJobDefinition?> FindAsync(ExpertJobType requested, CancellationToken ct) =>
            ValueTask.FromResult<ExpertJobDefinition?>(requested == type ? definition : null);
        public ValueTask<ExpertJobType?> FindDrawingJobAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<ExpertJobType?>(itemId == Recipe ? type : null);
    }
}
