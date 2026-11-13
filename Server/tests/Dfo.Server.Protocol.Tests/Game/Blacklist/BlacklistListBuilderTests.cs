using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests.Game.Blacklist;

[Trait("Category", "Unit")]
public sealed class BlacklistListBuilderTests
{
    private static readonly CharacterId Owner = new(1);
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.CreateCustomTimeZone("test+9", TimeSpan.FromHours(9), "test+9", "test+9");

    [Fact]
    public async Task Entries_keep_the_relation_order_and_take_the_current_name()
    {
        var relations = new Relations(
            new(new(3), new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new(new(2), new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)));
        var identities = new Identities(Identity(2, "two"), Identity(3, "three"));

        var listing = await BlacklistListBuilder.BuildAsync(Owner, relations, identities, TimeZoneInfo.Utc, default);

        Assert.Equal(["three", "two"], listing.Response.Entries.Select(entry => System.Text.Encoding.UTF8.GetString(entry.Name)));
        Assert.Empty(listing.Skipped);
        Assert.Equal(CharacterRelationKind.Blacklist, relations.AskedKind);
    }

    [Fact]
    public async Task The_date_is_the_day_the_relation_began_in_the_given_zone()
    {
        var since = new DateTimeOffset(2026, 3, 31, 20, 0, 0, TimeSpan.Zero);
        var relations = new Relations(new CharacterRelation(new(2), since));
        var identities = new Identities(Identity(2, "two"));

        var utc = await BlacklistListBuilder.BuildAsync(Owner, relations, identities, TimeZoneInfo.Utc, default);
        var tokyo = await BlacklistListBuilder.BuildAsync(Owner, relations, identities, Tokyo, default);

        Assert.Equal(new DateOnly(2026, 3, 31), Assert.Single(utc.Response.Entries).Since);
        Assert.Equal(new DateOnly(2026, 4, 1), Assert.Single(tokyo.Response.Entries).Since);
    }

    [Fact]
    public async Task A_name_the_client_cannot_read_is_left_out_and_reported()
    {
        var relations = new Relations(
            new(new(2), DateTimeOffset.UnixEpoch),
            new(new(3), DateTimeOffset.UnixEpoch),
            new(new(4), DateTimeOffset.UnixEpoch));
        var identities = new Identities(
            Identity(2, new string('a', 31)),
            Identity(3, new string('b', 30)),
            new CharacterPublicIdentity(new(4), [], 1, 0, 0));

        var listing = await BlacklistListBuilder.BuildAsync(Owner, relations, identities, TimeZoneInfo.Utc, default);

        Assert.Equal(new string('b', 30), System.Text.Encoding.UTF8.GetString(Assert.Single(listing.Response.Entries).Name));
        Assert.Equal([new CharacterId(2), new CharacterId(4)], listing.Skipped.Select(identity => identity.Character));
    }

    [Fact]
    public async Task A_target_that_is_no_longer_live_is_simply_absent()
    {
        var relations = new Relations(new CharacterRelation(new(2), DateTimeOffset.UnixEpoch), new CharacterRelation(new(3), DateTimeOffset.UnixEpoch));
        var identities = new Identities(Identity(3, "three"));

        var listing = await BlacklistListBuilder.BuildAsync(Owner, relations, identities, TimeZoneInfo.Utc, default);

        Assert.Single(listing.Response.Entries);
        Assert.Empty(listing.Skipped);
    }

    [Fact]
    public async Task No_relations_is_an_empty_list_without_reading_identities()
    {
        var identities = new Identities();

        var listing = await BlacklistListBuilder.BuildAsync(Owner, new Relations(), identities, TimeZoneInfo.Utc, default);

        Assert.Empty(listing.Response.Entries);
        Assert.Equal(0, identities.Reads);
    }

    private static CharacterPublicIdentity Identity(int id, string name) =>
        new(new(id), System.Text.Encoding.UTF8.GetBytes(name), 30, 1, 0);

    private sealed class Relations(params CharacterRelation[] relations) : ICharacterRelationQueryDomainService
    {
        public CharacterRelationKind? AskedKind { get; private set; }

        public ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken)
        {
            Assert.Equal(Owner, owner);
            AskedKind = kind;
            return ValueTask.FromResult<IReadOnlyList<CharacterRelation>>(relations);
        }

        public ValueTask<IReadOnlyList<CharacterId>> ObserversOfAsync(CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<bool> ExistsAsync(CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

    }

    private sealed class Identities(params CharacterPublicIdentity[] live) : ICharacterIdentityQueryDomainService
    {
        public int Reads { get; private set; }

        public ValueTask<CharacterPublicIdentity?> FindByNameAsync(byte[] name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>> ReadAsync(
            IReadOnlyCollection<CharacterId> characters, CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>>(
                live.Where(identity => characters.Contains(identity.Character)).ToDictionary(identity => identity.Character));
        }
    }
}
