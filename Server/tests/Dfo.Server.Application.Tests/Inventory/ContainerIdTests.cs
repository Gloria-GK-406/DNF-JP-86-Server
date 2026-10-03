using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// Reading back the owner of a container identifier.
/// </summary>
/// <remarks>
/// The identifiers are compared as written wherever containers are held -- as dictionary keys in
/// both stores, and as the thing a transaction dedupes enlistment on. So the question these fix is
/// not merely whether a spelling parses, but whether two spellings of one container can both be
/// accepted: if they could, a session would write through one and read back through the other and
/// not find its own write.
/// </remarks>
public sealed class ContainerIdTests
{
    [Fact]
    public void AccountIdentityIncludesItsContainerTypeAndKeepsTheCargoSpelling()
    {
        foreach (var expected in Enum.GetValues<InventoryContainerType>())
        {
            var id = ContainerId.ForAccount(new AccountId(7), expected);
            Assert.True(id.TryGetOwner(out var owner, out var type));
            Assert.Equal(new ContainerOwner(InventoryOwnerType.Account, 7), owner);
            Assert.Equal(expected, type);
            Assert.True(id.TryGetAccountOwner(out var account, out type));
            Assert.Equal(new AccountId(7), account);
            Assert.Equal(expected, type);
            Assert.False(id.TryGetCharacterOwner(out _, out _));
            Assert.Equal(expected == InventoryContainerType.AccountCargo, id.TryGetAccountCargoOwner(out _));
        }
        Assert.Equal(new ContainerId("account:7:AccountCargo"), ContainerId.ForAccountCargo(new AccountId(7)));
        Assert.False(ContainerId.ForCharacter(new CharacterId(7), InventoryContainerType.Main)
            .TryGetAccountOwner(out _, out _));
    }

    [Theory]
    [InlineData("account:7:12")]
    [InlineData("account:7:Main, Avatar")]
    [InlineData("account:7: Main")]
    [InlineData("account:7:main")]
    [InlineData("account:+7:Main")]
    [InlineData("account:07:Main")]
    [InlineData("account: 7 :Main")]
    [InlineData("account:0:Main")]
    [InlineData("account:-7:Main")]
    [InlineData("account:2147483648:Main")]
    [InlineData("account:7:Unknown")]
    [InlineData("account:7:Main:extra")]
    [InlineData("character:0:Main")]
    [InlineData("run:7:Main")]
    [InlineData(null)]
    public void InvalidPersistentIdentitiesNeverProduceAnOwner(string? value)
    {
        var id = value is null ? default : new ContainerId(value);
        Assert.False(id.TryGetOwner(out _, out _));
        Assert.False(id.TryGetAccountOwner(out _, out _));
        Assert.False(id.TryGetCharacterOwner(out _, out _));
    }

    [Fact]
    public void AMintedIdentifierReadsBackAsWhatItWasMintedFrom()
    {
        var id = ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Pet);

        Assert.True(id.TryGetCharacterOwner(out var characterId, out var type));
        Assert.Equal(new CharacterId(60), characterId);
        Assert.Equal(InventoryContainerType.Pet, type);
        Assert.True(id.TryGetOwner(out var owner, out _));
        Assert.Equal(new ContainerOwner(InventoryOwnerType.Character, 60), owner);
    }

    /// <remarks>
    /// Each of these names a container that <see cref="ContainerId.ForCharacter"/> would have
    /// spelled differently, so each would be a second container to everything that keys on the
    /// identifier. `7` and `Main, Avatar` are the ones worth naming: enum parsing accepts a bare
    /// number for a member that exists, and adds a comma list together even where the enum is not
    /// a set of flags -- `Main, Avatar` arrives as `Avatar`.
    /// </remarks>
    [Theory]
    [InlineData("character:60:7")]
    [InlineData("character:60:  7  ")]
    [InlineData("character:60:Main, Avatar")]
    [InlineData("character:60: Main")]
    [InlineData("character: 60 :Main")]
    [InlineData("character:+60:Main")]
    [InlineData("character:060:Main")]
    [InlineData("character:60:main")]
    public void ASecondSpellingOfOneContainerIsRefused(string value)
    {
        Assert.False(new ContainerId(value).TryGetCharacterOwner(out _, out _));
    }

    [Theory]
    [InlineData("character:60:5")]
    [InlineData("character:60:Cargo")]
    [InlineData("character:60:Main:extra")]
    [InlineData("character:60")]
    [InlineData("run:1:Main")]
    [InlineData("")]
    [InlineData("character:sixty:Main")]
    public void AnIdentifierMintedAnywhereElseIsRefused(string value)
    {
        Assert.False(new ContainerId(value).TryGetCharacterOwner(out _, out _));
    }

    /// <remarks>
    /// An identifier that was never minted at all answers false like any other, rather than
    /// throwing -- a method shaped to answer whether it can should not have a case where it cannot.
    /// </remarks>
    [Fact]
    public void AnIdentifierThatWasNeverMintedIsRefused()
    {
        Assert.False(default(ContainerId).TryGetCharacterOwner(out _, out _));
    }

    /// <remarks>
    /// Every container type a character has must survive the round trip, or the store would refuse
    /// a container the rest of the server hands it.
    /// </remarks>
    [Fact]
    public void EveryContainerTypeSurvivesTheRoundTrip()
    {
        foreach (var expected in Enum.GetValues<InventoryContainerType>())
        {
            var id = ContainerId.ForCharacter(new CharacterId(7), expected);

            Assert.True(id.TryGetCharacterOwner(out var characterId, out var type));
            Assert.Equal(new CharacterId(7), characterId);
            Assert.Equal(expected, type);
        }
    }
}
