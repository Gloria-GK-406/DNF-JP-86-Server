using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Creatures;

/// <summary>The state of one creature item. Its item UID is its only instance identity.</summary>
public sealed record CreatureState(
    int ItemUid,
    int ItemTemplateId,
    int Stomach,
    int Experience,
    int Level,
    byte[] NameBytes,
    int ModeFlag,
    int Mode1Field0A,
    int Mode1Field0B,
    int TailFlag)
{
    // Online time accounting may retain fractions; storage still carries the legacy whole value.
    private readonly double? exactStomach;
    private readonly int? exactStomachBase;
    public double ExactStomach
    {
        get => this.exactStomachBase == Stomach ? this.exactStomach ?? Stomach : Stomach;
        init
        {
            this.exactStomach = value;
            this.exactStomachBase = Stomach;
        }
    }
}

public interface ICreatureDomainStore : IDomainStore<ICreatureDomainStoreSession>;

public interface ICreatureDomainStoreSession
{
    ValueTask<CreatureState?> ReadAsync(CharacterId owner, int itemUid, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CharacterId owner, CancellationToken cancellationToken);
    ValueTask<CreatureState?> ReadCurrentAsync(CharacterId owner, CancellationToken cancellationToken);
    void Write(CharacterId owner, CreatureState creature);
    void ReviveIfEmpty(CharacterId owner, int itemUid);
}
