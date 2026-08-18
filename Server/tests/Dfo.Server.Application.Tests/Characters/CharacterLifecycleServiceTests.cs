using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
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

    private static (ICharacterDomainService Service, FakeRepository Repository) Build(
        params CharacterSummary[] existing) => BuildWith(new FakeStatSource(), existing);

    private static (ICharacterDomainService Service, FakeRepository Repository) BuildWith(
        ICharacterStatDefinitionSource stats,
        params CharacterSummary[] existing)
    {
        var repository = new FakeRepository(existing);
        var services = new ServiceCollection();
        services.AddSingleton<ICharacterRepository>(repository);
        services.AddSingleton(stats);
        services.AddSingleton<IInitialSkillDefinitionSource>(new FakeInitialSkillSource());
        new ApplicationModule().ConfigureServices(
            services,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        return (services.BuildServiceProvider().GetRequiredService<ICharacterDomainService>(), repository);
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
    public async Task CreationFillsTheLowestFreeSlot()
    {
        // Was "one past the highest in use", to avoid renumbering entries after a reused slot. The
        // client keeps no numbering across roster refreshes, so that bought nothing and let slots
        // drift above the count -- and eventually above the advertised limit, since the fullness
        // guard counts characters while the slot counted deletions too.
        var (service, repository) = Build(Existing(0, "A"), Existing(3, "B"));

        var result = await service.CreateCharacterAsync(
            Account, 2, "Cid"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterCreationStatus.Created, result.Status);
        Assert.Equal(1, repository.LastSpec!.SlotIndex);
        Assert.Equal(2, repository.LastSpec.Job);
    }

    [Fact]
    public async Task SlotsStayInsideTheAdvertisedLimitAfterDeleteAndCreateCycles()
    {
        // The old policy handed out slot 32 here -- past the 32-slot limit the roster header
        // advertises -- because only one character was left to count.
        var (service, repository) = Build(Existing(31, "Survivor"));

        var result = await service.CreateCharacterAsync(
            Account, 0, "Next"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterCreationStatus.Created, result.Status);
        Assert.Equal(0, repository.LastSpec!.SlotIndex);
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
    public async Task AnIndexThatMatchesNothingReportsItEmptyInsteadOfThrowing()
    {
        var (service, _) = Build(Existing(0, "Only"));

        var result = await service.DeleteCharacterAsync(
            Account, 5, "Nobody"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterDeletionStatus.SlotEmpty, result.Status);
    }

    [Fact]
    public async Task TheNameDecidesWhenTheIndexDoesNotLineUpWithASlot()
    {
        // The index the client sends is a position in its own character vector, which equals the
        // slot only while the roster has no gaps. Refusing here would make deletion impossible on
        // any account that has ever deleted a middle character -- and the name is the stronger
        // field anyway: the player typed it and the client checked it against the entry before
        // sending, so it cannot name a character the player did not point at.
        var (service, repository) = Build(Existing(0, "Only"));

        var result = await service.DeleteCharacterAsync(
            Account, 5, "Only"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterDeletionStatus.Deleted, result.Status);
        Assert.Equal(0, result.SlotIndex);
        Assert.Single(repository.Deleted);
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

    [Fact]
    public async Task TheSelectionIndexIsARosterPositionNotASlot()
    {
        // The client indexes its own character vector, filled from the roster in the order the
        // roster listed. Position and slot agree only on a gap-free roster -- which every earlier
        // sample happened to be, which is why this went unnoticed until a roster with a hole.
        var (service, repository) = Build(
            Existing(0, "First"), Existing(2, "Second"), Existing(3, "Third"));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await service.LoadByRosterPositionAsync(Account, 1, CancellationToken.None));

        Assert.Equal((ushort)2, repository.RequestedSlot);
    }

    [Fact]
    public async Task APositionPastTheEndOfTheRosterNeverReachesTheSlotLookup()
    {
        var (service, repository) = Build(Existing(0, "Only"));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await service.LoadByRosterPositionAsync(Account, 3, CancellationToken.None));

        Assert.Null(repository.RequestedSlot);
    }

    [Fact]
    public async Task ACreatedCharacterIsBornWithTheStatsItsJobDeclares()
    {
        // Without this the character reaches storage with every stat at zero, which the schema
        // accepts and the player then meets as a character that cannot act.
        var stats = new FakeStatSource();
        var (service, repository) = BuildWith(stats);

        var result = await service.CreateCharacterAsync(
            Account, 4, "Cleric"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterCreationStatus.Created, result.Status);
        Assert.Equal([(byte)4], stats.Requested);
        Assert.Equal(FakeStatSource.For(4), repository.LastSpec!.Stats);
    }

    /// <remarks>
    /// Its skills travel with it in the same specification, and therefore into the same
    /// transaction. A character created without them meets the player as an empty skill bar, which
    /// is how this was noticed: the tutorial highlights the attack keys in one of its rooms and
    /// there was nothing on them.
    /// <para>
    /// The slots follow the archive's declared order rather than the skill identifiers, because
    /// that order is the only thing that puts the two attack skills where the client expects them.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ACreatedCharacterIsBornHoldingTheSkillsItsJobDeclares()
    {
        var (service, repository) = Build();

        var result = await service.CreateCharacterAsync(
            Account, 4, "Cleric"u8.ToArray(), CancellationToken.None);

        Assert.Equal(CharacterCreationStatus.Created, result.Status);
        var declared = FakeInitialSkillSource.For(4);
        Assert.Equal(
            [
                new NewCharacterSkill(0, declared[0].SkillId, declared[0].Level),
                new NewCharacterSkill(1, declared[1].SkillId, declared[1].Level),
            ],
            repository.LastSpec!.InitialSkills);
    }

    [Fact]
    public async Task ARefusedCreationNeverReachesTheArchive()
    {
        // The stat lookup opens the archive, so it belongs after the cheap rejections rather than
        // before them: a bad name or a full roster should not need static content to be present.
        var stats = new FakeStatSource();
        var (service, _) = BuildWith(stats);

        await service.CreateCharacterAsync(Account, 13, "Valid"u8.ToArray(), CancellationToken.None);
        await service.CreateCharacterAsync(Account, 0, "x"u8.ToArray(), CancellationToken.None);

        Assert.Empty(stats.Requested);
    }

    /// <summary>Two grants per job, distinct per job so a lookup for the wrong one is visible.</summary>
    private sealed class FakeInitialSkillSource : IInitialSkillDefinitionSource
    {
        public static IReadOnlyList<InitialSkillGrant> For(byte job) =>
            [new InitialSkillGrant((uint)(500 + job), 1), new InitialSkillGrant((uint)(600 + job), 2)];

        public ValueTask<IReadOnlyList<InitialSkillGrant>> GetAsync(
            byte job,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(For(job));
    }

    private sealed class FakeStatSource : ICharacterStatDefinitionSource
    {
        public List<byte> Requested { get; } = [];

        /// <summary>Distinct per job, so a lookup for the wrong one is visible.</summary>
        public static CharacterBaseStats For(byte job) =>
            new(HpMax: 1000 + job, MpMax: 2000 + job, MoveSpeed: 8500);

        public ValueTask<CharacterBaseStats> GetInitialAsync(
            byte job,
            CancellationToken cancellationToken)
        {
            Requested.Add(job);
            return ValueTask.FromResult(For(job));
        }
    }

    private sealed class FakeRepository(CharacterSummary[] existing) : ICharacterRepository
    {
        public NewCharacterSpec? LastSpec { get; private set; }

        public List<CharacterId> Deleted { get; } = [];

        public ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CharacterSummary>>(existing);

        public ushort? RequestedSlot { get; private set; }

        // Records the slot the position was translated into. Returning null makes the load fail,
        // which is fine: the translation has already happened by then and is what is under test.
        public ValueTask<ActiveCharacterState?> FindByAccountAndSlotAsync(
            AccountId accountId,
            ushort slotIndex,
            CancellationToken cancellationToken)
        {
            RequestedSlot = slotIndex;
            return ValueTask.FromResult<ActiveCharacterState?>(null);
        }

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
