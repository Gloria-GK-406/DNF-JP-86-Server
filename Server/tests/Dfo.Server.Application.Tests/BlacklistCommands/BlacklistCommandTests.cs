using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.BlacklistCommands;

/// <summary>
/// CMD 0x0076 – 0x0078 through the real decoder, dispatcher, command and outbox, over a real SQLite
/// character store. Character 1 ("shop1") is the requester in every case.
/// </summary>
public sealed class BlacklistCommandTests
{
    private const ushort Register = 0x0076;
    private const ushort Delete = 0x0077;
    private const ushort List = 0x0078;

    /// <summary>The server's local zone in these tests: far enough from UTC that a date can move.</summary>
    private static readonly TimeZoneInfo ServerZone = TimeZoneInfo.CreateCustomTimeZone("test+9", TimeSpan.FromHours(9), "test+9", "test+9");

    /// <summary>The fixture clock reads 2026-09-08 20:00 at UTC+9.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Blacklisting_an_offline_character_answers_its_current_name_and_the_list_then_holds_it()
    {
        using var w = new BlacklistWorld();
        w.AddCharacter(201, "target");

        await w.SendAsync(Register, Name("target"));
        await w.SendAsync(List, []);

        Assert.Equal(
            [
                "01" + "06000000" + Hex("target"),
                "01" + "01" + "06000000" + Hex("target") + "7E" + "08" + "08",
            ],
            w.Bodies());
        Assert.Equal(1, w.Rows(201));
    }

    [Fact]
    public async Task Blacklisting_twice_is_refused_as_a_duplicate()
    {
        using var w = new BlacklistWorld();
        w.AddCharacter(201, "target");

        await w.SendAsync(Register, Name("target"));
        await w.SendAsync(Register, Name("target"));

        Assert.Equal("004A", w.Bodies()[1]);
        Assert.Equal(1, w.Rows(201));
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("gone")]
    public async Task Blacklisting_a_name_no_live_character_has_is_refused_as_missing(string name)
    {
        using var w = new BlacklistWorld();
        w.AddCharacter(201, "gone", deleted: true);

        await w.SendAsync(Register, Name(name));

        Assert.Equal("004C", Assert.Single(w.Bodies()));
        Assert.Equal(0, w.Rows(201));
    }

    [Fact]
    public async Task Blacklisting_onto_a_full_list_is_refused_as_full()
    {
        using var w = new BlacklistWorld();
        for (var id = 300; id < 364; id++)
        {
            w.AddCharacter(id, $"filler{id}");
            w.Blacklist(id, Now);
        }
        w.AddCharacter(201, "target");

        await w.SendAsync(Register, Name("target"));

        Assert.Equal("004E", Assert.Single(w.Bodies()));
        Assert.Equal(0, w.Rows(201));
    }

    [Fact]
    public async Task Blacklisting_oneself_is_refused_with_reason_zero()
    {
        using var w = new BlacklistWorld();

        await w.SendAsync(Register, Name("shop1"));

        Assert.Equal("0000", Assert.Single(w.Bodies()));
        Assert.Equal(0, w.Fixture.Scalar("SELECT COUNT(*) FROM character_relations"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("02000000616263")]
    [InlineData("0400000061")]
    public async Task A_malformed_register_or_delete_request_is_refused_and_changes_nothing(string hex)
    {
        using var w = new BlacklistWorld();
        w.AddCharacter(201, "target");
        w.Blacklist(201, Now);

        await w.SendAsync(Register, Convert.FromHexString(hex));
        await w.SendAsync(Delete, Convert.FromHexString(hex));

        Assert.Equal(["0000", "004B"], w.Bodies());
        Assert.Equal(1, w.Rows(201));
    }

    [Fact]
    public async Task Removing_a_blacklisted_character_answers_its_name_and_the_list_no_longer_holds_it()
    {
        using var w = new BlacklistWorld();
        w.AddCharacter(201, "target");
        w.Blacklist(201, Now);

        await w.SendAsync(Delete, Name("target"));
        await w.SendAsync(List, []);

        Assert.Equal(["01" + "06000000" + Hex("target"), "0100"], w.Bodies());
        Assert.Equal(0, w.Rows(201));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("nobody")]
    public async Task Removing_what_is_not_on_the_list_is_refused_with_75(string name)
    {
        using var w = new BlacklistWorld();
        w.AddCharacter(201, "target");

        await w.SendAsync(Delete, Name(name));

        Assert.Equal("004B", Assert.Single(w.Bodies()));
    }

    [Fact]
    public async Task Removing_a_name_too_long_for_the_reply_still_removes_it_and_answers_75()
    {
        using var w = new BlacklistWorld();
        var legacy = new string('x', 31);
        w.AddCharacter(201, legacy);
        w.Blacklist(201, Now);

        await w.SendAsync(Delete, Name(legacy));

        Assert.Equal("004B", Assert.Single(w.Bodies()));
        Assert.Equal(0, w.Rows(201));
    }

    [Fact]
    public async Task The_list_dates_each_entry_by_the_servers_local_day_and_leaves_out_names_the_client_cannot_read()
    {
        using var w = new BlacklistWorld();
        w.AddCharacter(201, "late");
        w.AddCharacter(202, new string('x', 31));
        w.AddCharacter(203, "early");
        // 20:00 UTC is already the next day at UTC+9.
        w.Blacklist(201, new DateTimeOffset(2026, 3, 31, 20, 0, 0, TimeSpan.Zero));
        w.Blacklist(202, new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero));
        w.Blacklist(203, new DateTimeOffset(2026, 4, 5, 14, 59, 0, TimeSpan.Zero));

        await w.SendAsync(List, []);

        Assert.Equal(
            "01" + "02"
            + "04000000" + Hex("late") + "7E" + "03" + "01"
            + "05000000" + Hex("early") + "7E" + "03" + "05",
            Assert.Single(w.Bodies()));
    }

    [Fact]
    public async Task An_empty_list_is_count_zero()
    {
        using var w = new BlacklistWorld();

        await w.SendAsync(List, []);

        Assert.Equal("0100", Assert.Single(w.Bodies()));
    }

    [Fact]
    public async Task Out_of_the_world_register_and_delete_are_refused_and_the_list_is_empty()
    {
        using var w = new BlacklistWorld();
        w.AddCharacter(201, "target");
        w.Blacklist(201, Now);
        w.LeaveWorld();

        await w.SendAsync(Register, Name("target"));
        await w.SendAsync(Delete, Name("target"));
        await w.SendAsync(List, []);

        Assert.Equal(["0000", "004B", "0100"], w.Bodies());
        Assert.Equal(1, w.Rows(201));
    }

    [Fact]
    public async Task Each_reply_is_a_cmd_frame_of_the_requests_type()
    {
        using var w = new BlacklistWorld();

        await w.SendAsync(Register, Name("shop1"));
        await w.SendAsync(Delete, Name("shop1"));
        await w.SendAsync(List, []);

        Assert.Equal([Register, Delete, List], w.Frames().Select(frame =>
        {
            Assert.Equal((byte)1, frame[0]);
            return BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));
        }));
    }

    private static byte[] Name(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        return [.. BitConverter.GetBytes(bytes.Length), .. bytes];
    }

    private static string Hex(string text) => Convert.ToHexString(Encoding.UTF8.GetBytes(text));

    private sealed class ZonedClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => zone;
    }

    private sealed class BlacklistWorld : IDisposable
    {
        private ushort sequence;

        public BlacklistWorld() => Fixture = new ShopFixture(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new ZonedClock(Now, ServerZone));
        });

        public ShopFixture Fixture { get; }

        private IServiceProvider Services => Fixture.Services;

        public void AddCharacter(int character, string name, bool deleted = false) =>
            Fixture.Sql($"""
                INSERT INTO accounts(account_id,m_id) VALUES({character},'bl{character}');
                INSERT INTO characters(character_id,account_id,name,job,level,slot_index,delete_flag) VALUES({character},{character},'{name}',12,30,0,{(deleted ? 1 : 0)});
                """);

        /// <summary>A row as an earlier blacklisting by character 1 would have left it.</summary>
        public void Blacklist(int target, DateTimeOffset at) =>
            Fixture.Sql($"INSERT INTO character_relations(owner_id,target_id,kind,created_at) VALUES(1,{target},2,{at.ToUnixTimeSeconds()});");

        public int Rows(int target) =>
            Fixture.Scalar($"SELECT COUNT(*) FROM character_relations WHERE owner_id=1 AND target_id={target} AND kind=2");

        public void LeaveWorld() =>
            TestGameplay.Run(Services, () =>
            {
                Assert.True(Services.GetRequiredService<ISelectedSessionDomainService>().ReleaseExact(Fixture.Session));
                Services.GetRequiredService<ITownAreaDomainService>().LeaveExact(Fixture.Session, TownAreaDepartureReason.SessionEnded);
            });

        public IReadOnlyList<byte[]> Frames() =>
            [.. Fixture.Sender.Outbound.Entries.Where(entry => !entry.IsClose && entry.Connection.ConnectionId == Fixture.Connection.ConnectionId)
                .SelectMany(entry => Fixture.Sender.Outbound.FramesOf(entry))];

        /// <summary>Each frame's body after the 15-byte header, in hex.</summary>
        public IReadOnlyList<string> Bodies() => [.. Frames().Select(frame => Convert.ToHexString(frame[15..]))];

        public async Task SendAsync(ushort type, byte[] body)
        {
            var next = ++sequence;
            var metadata = new GameMessageMetadata(1, type, 0, next) { InboundOrdinal = next, SequenceIsFresh = true }.WithRawBody(body);
            var registration = Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == type);
            var decoded = ((IGameMessageDecoder)Services.GetRequiredService(registration.DecoderType)).Decode(metadata, body)
                ?? throw new InvalidOperationException("The decoder declined the body.");
            await Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
                new NetworkDispatchContext(Fixture.Connection), decoded, default);
        }

        public void Dispose() => Fixture.Dispose();
    }
}
