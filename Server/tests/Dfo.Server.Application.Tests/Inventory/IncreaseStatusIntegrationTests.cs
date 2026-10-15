using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class IncreaseStatusIntegrationTests
{
    private const int Book = 690001555;

    [SkippableTheory]
    [InlineData(690001552, 1000)]
    [InlineData(690001553, 10000)]
    [InlineData(690001554, 100000)]
    [InlineData(690001555, -1)]
    public async Task Real_pvf_books_use_the_actual_001e_entry_and_commit_the_declared_experience(int item, int fixedGain)
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")), "PVF_ARCHIVE_PATH is not available.");
        using var fixture = new CeraShopFixture(useArchive: true);
        fixture.Host.SetLevel(20);
        await fixture.Host.Seed(5, item, 1);
        var before = fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var gain = fixedGain > 0 ? fixedGain : checked((int)((fixture.Host.ExperienceForLevel(21) - fixture.Host.ExperienceForLevel(20)) / 100));
        await Send(fixture, new());
        Assert.Equal(before + gain, fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(0, fixture.Host.Count(item));
        var frames = fixture.Host.Sender.Messages.Select(fixture.Encode).ToArray();
        Assert.Equal(Convert.FromHexString("010500FF0000000000000000"), frames[0][15..]);
        Assert.Contains(frames.Skip(1), f => Type(f) == 0x000E && BitConverter.ToInt32(f, 20) == -1);
        Assert.Contains(frames.Skip(1), f => Type(f) == 0x0025);
    }

    [Fact]
    public async Task Real_request_finishes_before_inventory_and_experience_projection_and_replay_does_not_pay_twice()
    {
        using var fixture = Fixture();
        await fixture.Host.Seed(5, Book, 2);
        var before = fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var counter = new GameInboundFrameCounter();
        await Send(fixture, counter);
        Assert.Equal(before + 1000, fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(1, fixture.Host.Count(Book));
        var frames = fixture.Host.Sender.Messages.Select(fixture.Encode).ToArray();
        Assert.Equal((ushort)0x001E, Type(frames[0]));
        Assert.Equal(Convert.FromHexString("010500FF0000000000000000"), frames[0][15..]);
        Assert.Contains(frames.Skip(1), f => Type(f) == 0x000E);
        Assert.Contains(frames.Skip(1), f => Type(f) == 0x0025);
        await Send(fixture, counter);
        Assert.Equal(before + 1000, fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(1, fixture.Host.Count(Book));
        var replay = Assert.Single(fixture.Host.Sender.Batches);
        Assert.DoesNotContain(replay.Frames, f => f[0] == 1 && Type(f) == 0x001E);
        Assert.Contains(replay.Frames, f => Type(f) == 0x0025);
    }

    [Theory]
    [InlineData(3, 20)]
    [InlineData(Book, 19)]
    [InlineData(Book, 85)]
    public async Task Unsupported_or_level_restricted_items_are_preserved_and_receive_a_failure_ack(int item, int level)
    {
        using var fixture = Fixture();
        fixture.Host.SetLevel((byte)level);
        await fixture.Host.Seed(5, item, 1);
        var before = fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");
        await Send(fixture, new());
        var response = fixture.Encode(fixture.Host.Sender.Messages[0]);
        Assert.Equal((ushort)0x001E, Type(response));
        Assert.Equal(17, response.Length);
        Assert.Equal(0, response[15]); // A21's refusal path pops the pending operation too.
        Assert.Equal(1, fixture.Host.Count(item));
        Assert.Equal(before, fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Experience_write_failure_rolls_back_the_book_and_returns_a_failure_ack()
    {
        using var fixture = Fixture();
        await fixture.Host.Seed(5, Book, 1);
        var before = fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");
        fixture.Host.Sql("CREATE TRIGGER fail_increase_status BEFORE UPDATE OF exp ON characters BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Send(fixture, new());
        Assert.Equal(1, fixture.Host.Count(Book));
        Assert.Equal(before, fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        var response = fixture.Encode(fixture.Host.Sender.Messages[0]);
        Assert.Equal(Convert.FromHexString("0002"), response[15..]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("05")]
    [InlineData("050000")]
    [InlineData("FFFF")]
    public void Request_rejects_truncation_trailing_bytes_and_negative_slots(string body)
    {
        using var fixture = Fixture();
        var registration = fixture.Host.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == 0x001E);
        var decoder = (IGameMessageDecoder)fixture.Host.Services.GetRequiredService(registration.DecoderType);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString(body)));
    }

    private static ushort Type(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
    private static async Task Send(CeraShopFixture fixture, GameInboundFrameCounter counter)
    {
        var registration = fixture.Host.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == 0x001E);
        var decoder = (IGameMessageDecoder)fixture.Host.Services.GetRequiredService(registration.DecoderType);
        var (ordinal, fresh) = counter.Observe(1);
        byte[] body = [5, 0];
        var metadata = new GameMessageMetadata(1, 0x001E, 0, 1) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }.WithRawBody(body);
        var request = decoder.Decode(metadata, body)!;
        await fixture.Host.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(fixture.Host.Connection), request, default);
    }
    private static CeraShopFixture Fixture()
    {
        var fixture = new CeraShopFixture(configure: services =>
        {
            services.AddSingleton<IExperienceItemDefinitionSource>(new Definitions());
            services.AddSingleton<IItemUseRuleDefinitionSource>(new FakeItemUseRules((Book, ItemUseRules.Potion(cooldownMilliseconds: 0))));
            services.AddSingleton<ICharacterSkillPointDomainService>(new Points());
        });
        fixture.Content.Placements[Book] = new(2, 100, new(BusinessInventorySpace.Consumable, 0, 55));
        fixture.Host.SetLevel(20);
        return fixture;
    }
    private sealed class Definitions : IExperienceItemDefinitionSource
    {
        public ValueTask<ExperienceItemDefinition?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<ExperienceItemDefinition?>(itemId == Book ? new(ExperienceItemGrantKind.Fixed, 1000, 20, 84, true) : null);
    }
    private sealed class Points : ICharacterSkillPointDomainService
    {
        private static readonly CharacterSkillPointLedger Ledger = new(321, 0, 0, 0, []);
        public ValueTask<CharacterSkillPointLedger?> LedgerAsync(CharacterId id, CancellationToken ct) => ValueTask.FromResult<CharacterSkillPointLedger?>(Ledger);
        public ValueTask<CharacterSkillProtocolMirrorState?> RemainingAsync(CharacterId id, CancellationToken ct) => ValueTask.FromResult<CharacterSkillProtocolMirrorState?>(Ledger.ToProtocolMirror());
    }
}
