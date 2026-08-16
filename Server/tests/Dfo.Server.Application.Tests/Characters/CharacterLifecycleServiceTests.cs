using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// The rules that decide whether a character may be created or deleted.
/// </summary>
public sealed class CharacterLifecycleServiceTests
{
    private static readonly AccountId Account = new(1);

    private static (ICharacterService Service, FakeRepository Repository) Build(
        params CharacterSummary[] existing)
    {
        var repository = new FakeRepository(existing);
        var services = new ServiceCollection();
        services.AddSingleton<ICharacterRepository>(repository);
        new ApplicationModule().ConfigureServices(
            services,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        return (services.BuildServiceProvider().GetRequiredService<ICharacterService>(), repository);
    }

    private static CharacterSummary Existing(ushort slot, string name) =>
        new(new CharacterId(slot + 100), slot, Encoding.UTF8.GetBytes(name), 0, 0, 1, 0);

    [Fact]
    public async Task AFreeNameIsAvailable()
    {
        var (service, _) = Build();

        Assert.Equal(
            CharacterNameStatus.Available,
            await service.CheckNameAsync("Nagisa"u8.ToArray(), CancellationToken.None));
    }

    [Fact]
    public async Task ANameAlreadyOnTheServerIsRefused()
    {
        var (service, _) = Build(Existing(0, "Nagisa"));

        Assert.Equal(
            CharacterNameStatus.AlreadyTaken,
            await service.CheckNameAsync("Nagisa"u8.ToArray(), CancellationToken.None));
    }

    [Fact]
    public async Task NameLengthIsCountedInBytesNotCharacters()
    {
        // Seven CJK characters are 21 UTF-8 bytes: fine for the 30-byte check, over the 18-byte
        // creation limit. The same name legitimately passes one protocol and fails the other.
        var (service, _) = Build();
        var sevenCjk = Encoding.UTF8.GetBytes("素早い黒い狐だ");
        Assert.Equal(21, sevenCjk.Length);

        Assert.Equal(
            CharacterNameStatus.Available,
            await service.CheckNameAsync(sevenCjk, CancellationToken.None));

        var creation = await service.CreateCharacterAsync(
            Account, 0, sevenCjk, CancellationToken.None);
        Assert.Equal(CharacterCreationStatus.InvalidName, creation.Status);
    }

    [Fact]
    public async Task InvalidUtf8IsRefusedRatherThanReplacedWithSubstituteCharacters()
    {
        var (service, _) = Build();

        Assert.Equal(
            CharacterNameStatus.Malformed,
            await service.CheckNameAsync([0xC3, 0x28, 0x41], CancellationToken.None));
    }

    [Fact]
    public async Task AJobBeyondTheKnownRangeIsRefused()
    {
        var (service, _) = Build();

        var result = await service.CreateCharacterAsync(
            Account, 13, "Valid"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterCreationStatus.MalformedRequest, result.Status);
    }

    [Fact]
    public async Task CreationTakesTheSlotAfterTheHighestInUse()
    {
        // Not the lowest free slot. The client addresses a character by its position in the
        // slot-ordered roster, so filling a gap would renumber everyone after it.
        var (service, repository) = Build(Existing(0, "A"), Existing(3, "B"));

        var result = await service.CreateCharacterAsync(
            Account, 2, "Cid"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterCreationStatus.Created, result.Status);
        Assert.Equal(4, repository.LastSpec!.SlotIndex);
        Assert.Equal(2, repository.LastSpec.Job);
    }

    [Fact]
    public async Task CreationIsRefusedWhenEverySlotIsUsed()
    {
        var full = Enumerable.Range(0, 32)
            .Select(index => Existing((ushort)index, $"Filled{index}"))
            .ToArray();
        var (service, _) = Build(full);

        var result = await service.CreateCharacterAsync(
            Account, 0, "OneMore"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterCreationStatus.NoFreeSlot, result.Status);
    }

    [Fact]
    public async Task DeletionRequiresTheConfirmationNameToMatchTheOccupant()
    {
        var (service, repository) = Build(Existing(0, "Keeper"));

        var result = await service.DeleteCharacterAsync(
            Account, 0, "Someone"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterDeletionStatus.NameMismatch, result.Status);
        Assert.Empty(repository.Deleted);
    }

    [Fact]
    public async Task DeletingAnEmptySlotReportsItEmptyInsteadOfThrowing()
    {
        var (service, _) = Build(Existing(0, "Only"));

        var result = await service.DeleteCharacterAsync(
            Account, 5, "Only"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterDeletionStatus.SlotEmpty, result.Status);
    }

    [Fact]
    public async Task AMatchingNameDeletesThatCharacter()
    {
        var (service, repository) = Build(Existing(0, "A"), Existing(1, "Doomed"));

        var result = await service.DeleteCharacterAsync(
            Account, 1, "Doomed"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterDeletionStatus.Deleted, result.Status);
        Assert.Equal(101, result.CharacterId!.Value.Value);
        Assert.Equal([101], repository.Deleted.Select(id => id.Value));
    }

    private sealed class FakeRepository(CharacterSummary[] existing) : ICharacterRepository
    {
        public NewCharacterSpec? LastSpec { get; private set; }

        public List<CharacterId> Deleted { get; } = [];

        public ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CharacterSummary>>(existing);

        public ValueTask<ActiveCharacterState?> FindByAccountAndSlotAsync(
            AccountId accountId,
            ushort slotIndex,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> NameExistsAsync(byte[] name, CancellationToken cancellationToken) =>
            ValueTask.FromResult(existing.Any(item => item.Name.AsSpan().SequenceEqual(name)));

        public ValueTask<CharacterId> CreateAsync(
            NewCharacterSpec spec,
            CancellationToken cancellationToken)
        {
            LastSpec = spec;
            return ValueTask.FromResult(new CharacterId(900));
        }

        public ValueTask<bool> SoftDeleteAsync(
            CharacterId characterId,
            CancellationToken cancellationToken)
        {
            Deleted.Add(characterId);
            return ValueTask.FromResult(true);
        }
    }
}
