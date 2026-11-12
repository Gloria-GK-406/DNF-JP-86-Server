using Dfo.Server.Data.Characters;
using static Dfo.Server.Application.Tests.Relations.RelationFixture;

namespace Dfo.Server.Application.Tests.Relations;

/// <summary>The registered identity read: by exact name, and several by identifier keyed for lookup.</summary>
public sealed class CharacterIdentityQueryDomainServiceTests
{
    [Fact]
    public async Task A_name_finds_the_live_character_and_a_deleted_or_unknown_one_finds_nothing()
    {
        await using var f = new RelationFixture();
        var beta = await f.Identities.FindByNameAsync("beta"u8.ToArray(), default);
        Assert.Equal(new CharacterPublicIdentityShape(B, "beta", 20, 1, 1), Shape(beta));
        Assert.Null(await f.Identities.FindByNameAsync("ghost"u8.ToArray(), default));
        Assert.Null(await f.Identities.FindByNameAsync("Beta"u8.ToArray(), default));
        Assert.Null(await f.Identities.FindByNameAsync([], default));
    }

    [Fact]
    public async Task Several_identifiers_come_back_keyed_by_character_with_the_deleted_left_out()
    {
        await using var f = new RelationFixture();
        var read = await f.Identities.ReadAsync([A, Deleted, E, A, new CharacterId(9999)], default);
        Assert.Equal([A, E], read.Keys.OrderBy(x => x.Value));
        Assert.Equal(new CharacterPublicIdentityShape(E, "epsilon", 50, 4, 2), Shape(read[E]));
        Assert.Empty(await f.Identities.ReadAsync([], default));
    }

    private static CharacterPublicIdentityShape? Shape(Dfo.Server.Abstractions.Characters.CharacterPublicIdentity? identity) =>
        identity is null ? null : new(identity.Character, System.Text.Encoding.UTF8.GetString(identity.Name),
            identity.Level, identity.Job, identity.GrowType);

    private sealed record CharacterPublicIdentityShape(CharacterId Character, string Name, int Level, int Job, int GrowType);
}
