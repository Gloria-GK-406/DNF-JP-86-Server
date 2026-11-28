using System.Buffers.Binary;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class ExpertJobProtocolFlowTests
{
    private const int ClearCube = 3037;
    private const ushort AcceptQuest = 0x001F;
    private const ushort FinishQuest = 0x0022;
    private const ushort GiveUp = 0x00EF;
    private const ushort ExpertState = 0x00CD;

    [SkippableTheory]
    [InlineData(2702, ExpertJobType.Enchanter, 191, 11007)]
    [InlineData(2708, ExpertJobType.Alchemist, 193, 11013)]
    [InlineData(2710, ExpertJobType.Disjointer, 194, 0)]
    [InlineData(2712, ExpertJobType.DollController, 192, 11016)]
    public async Task Real_PVF_job_quest_joins_only_its_profession_and_giveup_reopens_the_quest(
        ushort quest, ExpertJobType type, int skill, ushort followUp)
    {
        using var host = ArchiveFixture();
        var fixture = host.Host;
        var profession = fixture.Services.GetRequiredService<IExpertJobDomainService>().Get(fixture.Session.CharacterId);
        await AssertOnlyProfession(profession, ExpertJobType.None);
        await fixture.SeedShared(ClearCube, 100);

        await SendQuest(fixture, AcceptQuest, quest);
        Assert.Equal(1, ActiveQuest(fixture, quest));
        var beforeReward = fixture.Sender.Outbound.Frames.Count;
        await SendQuest(fixture, FinishQuest, quest);
        var reward = fixture.Sender.Outbound.Frames.Skip(beforeReward).ToArray();

        Assert.Equal(0, ActiveQuest(fixture, quest));
        Assert.Equal(0, fixture.SharedCount(ClearCube));
        Assert.Equal(2, LearnedPages(fixture, skill));
        var state = await profession.GetStateAsync(default);
        Assert.Equal(type, state.Type);
        Assert.Equal(0u, state.Experience);
        Assert.Equal(type == ExpertJobType.Enchanter ? 300 : 0, state.EnchanterEndurance);
        Assert.Equal(type == ExpertJobType.Disjointer ? 1 : 0, state.DisjointMachineGrade);
        Assert.Equal(type == ExpertJobType.Disjointer ? 300 : 0, state.DisjointMachineEndurance);
        await AssertOnlyProfession(profession, type);
        var rewardState = Array.FindIndex(reward, frame => IsFrame(frame, 0, ExpertState));
        var rewardAck = Array.FindIndex(reward, frame => IsFrame(frame, 1, FinishQuest));
        Assert.True(rewardState >= 0 && rewardAck > rewardState);
        Assert.Equal((byte)type, reward[rewardState][16]);
        Assert.Equal(1, reward[rewardAck][15]);

        var otherQuest = quest == 2702 ? (ushort)2708 : (ushort)2702;
        await SendQuest(fixture, AcceptQuest, otherQuest);
        Assert.Equal(0, ActiveQuest(fixture, otherQuest));

        if (followUp != 0)
        {
            await SendQuest(fixture, AcceptQuest, followUp);
            Assert.Equal(1, ActiveQuest(fixture, followUp));
        }

        var gold = fixture.Gold;
        var beforeGiveUp = fixture.Sender.Outbound.Frames.Count;
        await Send(fixture, GiveUp, []);
        var giveUp = fixture.Sender.Outbound.Frames.Skip(beforeGiveUp).ToArray();

        Assert.Equal(gold - 1000, fixture.Gold);
        Assert.Equal(0, LearnedPages(fixture, skill));
        var after = await profession.GetStateAsync(default);
        Assert.Equal(ExpertJobType.None, after.Type);
        Assert.Equal(1, after.GiveUpCount);
        Assert.Equal(0, after.DisjointMachineEndurance);
        Assert.Equal(0, after.EnchanterEndurance);
        Assert.Empty(after.LearnedRecipeIds);
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_expert_job_recipes WHERE character_id=1"));
        await AssertOnlyProfession(profession, ExpertJobType.None);
        Assert.Equal(new byte[] { 1, 0 }, Assert.Single(giveUp, frame => IsFrame(frame, 0, ExpertState))[15..]);
        Assert.Equal(1, Assert.Single(giveUp, frame => IsFrame(frame, 1, GiveUp))[15]);

        if (followUp != 0)
            Assert.Equal(0, ActiveQuest(fixture, followUp));
        await SendQuest(fixture, AcceptQuest, quest);
        Assert.Equal(1, ActiveQuest(fixture, quest));
    }

    [SkippableFact]
    public async Task Real_PVF_job_quest_rolls_back_materials_skills_and_quest_when_profession_write_fails()
    {
        const ushort quest = 2708;
        using var host = ArchiveFixture();
        var fixture = host.Host;
        await fixture.SeedShared(ClearCube, 100);
        await SendQuest(fixture, AcceptQuest, quest);
        Assert.Equal(1, ActiveQuest(fixture, quest));
        fixture.Sql("""
            CREATE TRIGGER fail_expert_profession BEFORE INSERT ON character_expert_job
            BEGIN SELECT RAISE(ABORT, 'fixture expert profession write'); END;
            """);

        var before = fixture.Sender.Outbound.Frames.Count;
        await SendQuest(fixture, FinishQuest, quest);
        var reply = fixture.Sender.Outbound.Frames.Skip(before).ToArray();

        Assert.Equal(new byte[] { 0, 22 }, Assert.Single(reply, frame => IsFrame(frame, 1, FinishQuest))[15..]);
        Assert.DoesNotContain(reply, frame => IsFrame(frame, 0, ExpertState));
        Assert.Equal(100, fixture.SharedCount(ClearCube));
        Assert.Equal(1, ActiveQuest(fixture, quest));
        Assert.Equal(0, LearnedPages(fixture, 193));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_expert_job WHERE character_id=1"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_expert_job_recipes WHERE character_id=1"));
    }

    [SkippableFact]
    public async Task Real_highest_enchant_drawing_checks_profession_experience_and_accepts_the_granted_level_one_skill()
    {
        const ushort enchanterQuest = 2702;
        const ushort useItem = 0x002C;
        const int drawing = 10015151;
        const short slot = 65;
        using var host = ArchiveFixture();
        var fixture = host.Host;
        await fixture.SeedShared(ClearCube, 100);
        await SendQuest(fixture, AcceptQuest, enchanterQuest);
        await SendQuest(fixture, FinishQuest, enchanterQuest);
        await fixture.Seed(slot, drawing, 1, 2);
        Assert.Equal(2, LearnedPages(fixture, 191));

        var request = new byte[11];
        BinaryPrimitives.WriteInt16LittleEndian(request, slot);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(7), drawing);
        await Send(fixture, useItem, request);
        Assert.Equal(0, fixture.Sender.Outbound.Frames.Last(frame => IsFrame(frame, 1, useItem))[15]);
        Assert.Equal(1, fixture.Count(drawing));

        var rules = fixture.Services.GetRequiredService<IExpertJobRuleService>();
        var enchanter = Assert.IsType<ExpertJobDefinition>(await rules.FindAsync(ExpertJobType.Enchanter, default));
        Assert.Equal(11, enchanter.Enchantment!.CardRecipes[drawing].RequiredLevel);
        var experience = enchanter.ExperienceThresholds[7];
        Assert.Equal(9, enchanter.LevelAt(experience));
        fixture.Sql($"UPDATE character_expert_job SET expert_job_exp={experience} WHERE character_id=1");
        var beforeLearning = fixture.Sender.Outbound.Frames.Count;
        await Send(fixture, useItem, request);
        var replies = fixture.Sender.Outbound.Frames.Skip(beforeLearning).ToArray();
        Assert.Equal(1, Assert.Single(replies, frame => IsFrame(frame, 1, useItem))[15]);
        Assert.Single(replies, frame => IsFrame(frame, 0, ExpertState));
        Assert.Equal(0, fixture.Count(drawing));
        Assert.Equal(2, LearnedPages(fixture, 191));
        var job = fixture.Services.GetRequiredService<IExpertJobDomainService>().Get(fixture.Session.CharacterId);
        Assert.Contains(drawing, (await job.GetStateAsync(default)).LearnedRecipeIds);
    }

    private static CeraShopFixture ArchiveFixture()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        var host = new CeraShopFixture(useArchive: true, questProtocol: true);
        host.Host.Sql("""
            UPDATE characters SET job=0 WHERE character_id=1;
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0);
            INSERT INTO character_settings(character_id) VALUES(1);
            INSERT INTO character_pvp_record(character_id) VALUES(1);
            INSERT INTO character_pvp_skill_state(character_id) VALUES(1);
            """);
        return host;
    }

    private static async Task AssertOnlyProfession(ICharacterExpertJob job, ExpertJobType type)
    {
        Assert.Equal(type == ExpertJobType.Enchanter, await job.GetEnchanterAsync(default) is not null);
        Assert.Equal(type == ExpertJobType.Alchemist, await job.GetAlchemistAsync(default) is not null);
        Assert.Equal(type == ExpertJobType.Disjointer, await job.GetDisjointerAsync(default) is not null);
        Assert.Equal(type == ExpertJobType.DollController, await job.GetDollControllerAsync(default) is not null);
    }

    private static int ActiveQuest(ShopFixture fixture, ushort quest) => fixture.Scalar(
        $"SELECT COUNT(*) FROM character_active_quests WHERE character_id=1 AND quest_id={quest}");

    private static int LearnedPages(ShopFixture fixture, int skill) => fixture.Scalar(
        $"SELECT COUNT(*) FROM character_skills WHERE character_id=1 AND mode='pve' AND skill_id={skill} AND level=1");

    private static ValueTask SendQuest(ShopFixture fixture, ushort opcode, ushort quest)
    {
        byte[] body = opcode == FinishQuest ? [0, 0, 0, 0, 0xFF, 0xFF, 1, 0, 0xFF, 0xFF] : new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(body, opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), quest);
        return Send(fixture, opcode, body);
    }

    private static ValueTask Send(ShopFixture fixture, ushort opcode, byte[] body) =>
        EffectRequestTransport.SendRaw(fixture, new(1, opcode, 0, EffectRequestTransport.FreshSequence(fixture)), body);

    private static bool IsFrame(byte[] frame, byte selector, ushort opcode) =>
        frame[0] == selector && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == opcode;
}
