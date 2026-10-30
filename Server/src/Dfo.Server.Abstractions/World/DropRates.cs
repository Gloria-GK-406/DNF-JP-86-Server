using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>
/// The archive's own rarity numbers.
/// </summary>
/// <remarks>
/// The names are the archive's: <c>Etc/ServerParameter.etc</c> lists seven of them in ascending
/// order under <c>[drop upgrade rarity]</c>, and an item's <c>[rarity]</c> takes exactly those
/// seven values. The pairing was checked against what the items are: rarity four holds the
/// 铁脊蛇矛 and 方天画戟, which are A21's level-90 epics, and rarity six holds 安徒恩的抵抗者,
/// which is a legendary.
/// <para>
/// A rarity ladder declares six bands, so only the first six are reachable through one. Nothing
/// is lost by that: no item in the archive that declares itself creatable is above epic.
/// </para>
/// </remarks>
public enum DropRarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Unique = 3,
    Epic = 4,
    Chronicle = 5,
    Legendary = 6,
}

/// <summary>What a rate slot produces when it hits.</summary>
/// <remarks>
/// <b>A reading, not a fact.</b> The archive names none of its five rates. That the first and
/// third yield stackables, the second equipment and the fourth an avatar is the reference server's
/// mapping and has no second source. What is checkable is only the shape around it: gold has no
/// rarity ladder and the other four do, so the first slot is the one without a ladder.
/// <para>
/// Getting this wrong swaps which pool a slot draws from. It would be visible immediately -- a
/// monster paying its equipment rate in recipes -- which is the kind of wrong that live play
/// corrects cheaply, unlike a rate that is merely off by a factor.
/// </para>
/// </remarks>
public enum DropItemKind
{
    Gold = 0,
    Stackable = 1,
    Equipment = 2,
    Avatar = 3,
}

/// <summary>What one slot of one occasion came to, before anything was drawn from it.</summary>
/// <param name="Slot">Which of the table's rates this is.</param>
/// <param name="Rate">
/// The rate after every multiplier, still a numerator over the basis. Kept as a real rather than
/// rounded: the archive declares whole numerators and real multipliers, and where their product
/// should round is not something the archive says.
/// </param>
public readonly record struct SlotRate(DropRateSlot Slot, double Rate);

/// <summary>The rates one occasion is about to roll, one per slot.</summary>
/// <remarks>
/// A record of its own so that the pity seam has something to adjust that is neither the table nor
/// the outcome. Everything the seam is allowed to change is in here.
/// </remarks>
public sealed record DropRates(IReadOnlyList<SlotRate> Slots, int Basis)
{
    /// <summary>What this slot came to, or zero when the occasion has no such slot.</summary>
    public double Of(DropRateSlot slot) =>
        this.Slots.FirstOrDefault(entry => entry.Slot == slot).Rate;

    /// <summary>The same rates with one slot replaced.</summary>
    public DropRates With(DropRateSlot slot, double rate) =>
        this with
        {
            Slots =
            [
                .. this.Slots.Select(
                    entry => entry.Slot == slot ? entry with { Rate = rate } : entry),
            ],
        };
}
