using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Game;

public sealed partial class SelectCharacterLifecycleTests
{
    /// <summary>RQ-038: the entry batch is composed from the selected character's real friend list and blacklist.</summary>
    [Fact]
    public async Task Entry_is_composed_with_the_selected_characters_own_friends_and_blacklist()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var relations = new SelectedCharacterRelations();
            var fixture = await Fixture.CreateAsync(relations: relations, identities: relations);
            var selected = fixture.Result.Character.Identity.CharacterId;
            relations.Owner = selected;

            await fixture.Handler.HandleAsync(fixture.Context, fixture.Command, CancellationToken.None);

            var handed = fixture.StateMessages.Relations!;
            var friend = Assert.Single(handed.Friends.Entries);
            Assert.Equal(("pal", (ushort)0, (byte)40, 3, (byte)1),
                (Encoding.UTF8.GetString(friend.Name), friend.Channel, friend.Level, friend.Job, friend.GrowType));
            Assert.Equal("foe", Encoding.UTF8.GetString(Assert.Single(handed.Blacklist.Entries).Name));
            Assert.Equal([CharacterRelationKind.Friend, CharacterRelationKind.Blacklist], relations.Asked);
        });
    }

    /// <summary>Character 777 "pal" is a friend and 778 "foe" is blacklisted, both offline; only the selected character holds them.</summary>
    private sealed class SelectedCharacterRelations : ICharacterRelationQueryDomainService, ICharacterIdentityQueryDomainService
    {
        public CharacterId Owner { get; set; }

        public List<CharacterRelationKind> Asked { get; } = [];

        public ValueTask<IReadOnlyList<CharacterRelation>> ListAsync(CharacterId owner, CharacterRelationKind kind, CancellationToken cancellationToken)
        {
            Assert.Equal(Owner, owner);
            Asked.Add(kind);
            return ValueTask.FromResult<IReadOnlyList<CharacterRelation>>(
                [new(new(kind == CharacterRelationKind.Friend ? 777 : 778), DateTimeOffset.UnixEpoch)]);
        }

        public ValueTask<IReadOnlyList<CharacterId>> ObserversOfAsync(CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<bool> ExistsAsync(CharacterId owner, CharacterId target, CharacterRelationKind kind, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public int Limit(CharacterRelationKind kind) => 64;

        public ValueTask<CharacterPublicIdentity?> FindByNameAsync(byte[] name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>> ReadAsync(
            IReadOnlyCollection<CharacterId> characters, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyDictionary<CharacterId, CharacterPublicIdentity>>(new Dictionary<CharacterId, CharacterPublicIdentity>
            {
                [new(777)] = new(new(777), "pal"u8.ToArray(), 40, 3, 1),
                [new(778)] = new(new(778), "foe"u8.ToArray(), 50, 4, 0),
            }.Where(entry => characters.Contains(entry.Key)).ToDictionary());
    }
}
