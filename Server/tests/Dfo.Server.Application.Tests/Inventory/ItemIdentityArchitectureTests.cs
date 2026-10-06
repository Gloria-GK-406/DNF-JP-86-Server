using System.Reflection;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Protocol.Channel.Connect;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// Nothing tells two of one pile apart, because nothing is able to.
/// </summary>
/// <remarks>
/// A pile is interchangeable by definition: two of one template with the same count are the same
/// thing, and there is no question anybody may ask that separates them. The way that goes wrong is
/// never a decision -- it is a number that happens to be lying about. A row key is such a number:
/// it is unique per pile, it reads like an identity, and code that starts ordering, matching or
/// reconciling by it looks correct right up until a pile is written again under a different key.
/// <para>
/// These are the two mechanical properties that keep such a number out of reach. An identity is
/// always optional wherever an item is described, so a pile can be handed none; and the key of the
/// row an item was read out of does not leave the gateway that read it, so nothing upstream has one
/// to be tempted by. Neither is a rule somebody has to remember while writing code -- they are
/// both refusals by the type, and this is what says the types still refuse.
/// </para>
/// </remarks>
public sealed class ItemIdentityArchitectureTests
{
    /// <summary>
    /// The vocabulary a storage row key is written in.
    /// </summary>
    /// <remarks>
    /// Names rather than types, because the offending value is always an ordinary integer -- the
    /// point of the whole change is that it stopped being called an identity. What is caught here
    /// is the name travelling upwards out of the gateway, which is how it would arrive somewhere
    /// that could distinguish piles by it.
    /// </remarks>
    private static readonly string[] RowKeyNames =
        ["StorageEntryId", "ItemUid", "ItemRowId", "ItemRowKey"];

    /// <summary>
    /// Where a row key is allowed to be said, each with the reason it is allowed.
    /// </summary>
    /// <remarks>
    /// One namespace and one assembly, and both of them are the gateway: the row records a table
    /// read answers with, and the SQL that reads them. A row key is a true fact about a row, and
    /// this is the only place whose subject is rows.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> RowKeyIsAllowedIn =
        new Dictionary<string, string>
        {
            ["Dfo.Server.Abstractions.Persistence"] =
                "行网关的行记录：它的主语就是行，行键是关于行的真话。",
            ["Dfo.Server.Persistence"] =
                "读写这些行的 SQL 本身。",
        };

    private static readonly Assembly[] Production =
    [
        typeof(InventoryItemState).Assembly,
        typeof(ItemReference).Assembly,
        typeof(ItemContainerOperations).Assembly,
        typeof(InventoryItemCore).Assembly,
        typeof(ChannelConnectHandler).Assembly,
    ];

    /// <summary>
    /// The sweep actually finds something.
    /// </summary>
    /// <remarks>
    /// Without this, a reflection query that stopped matching -- a moved assembly, a renamed
    /// type -- would turn both checks below into assertions about an empty sequence, and an empty
    /// sequence has no violations in it.
    /// </remarks>
    [Fact]
    public void TheSweepFindsTheInventoryShapes()
    {
        Assert.Equal(5, Production.Distinct().Count());
        Assert.Contains(Members(), member => member.Name == nameof(InventoryItemState.Identity));
        Assert.Contains(Members(), member => member.Name == "ItemUid");
    }

    /// <summary>
    /// An identity is optional everywhere it is said, so a pile can be given none.
    /// </summary>
    /// <remarks>
    /// Every shape that names an item can also name a pile -- one bag holds both -- so a member
    /// that could not be absent would be one a pile was handed anyway. That is the whole failure
    /// mode written out: give a pile a number of its own and somebody downstream will separate two
    /// of them by it.
    ///
    /// Said over the member's type rather than over a list of approved shapes, so a shape added
    /// later is covered without anybody remembering to add it.
    /// </remarks>
    [Fact]
    public void AnIdentityIsAlwaysOptionalWhereverAnItemIsDescribed()
    {
        var violations = Members()
            .Where(member => member.Type == typeof(ItemIdentity))
            // Equipment-only custody references cannot describe a pile; their identity is required.
            .Where(member => member.Owner != typeof(Dfo.Server.Data.Mail.MailAsset.Equipment)
                && member.Owner != typeof(Dfo.Server.Abstractions.Effects.EquipmentEffectTarget))
            .Select(member => $"{member.Owner.FullName}.{member.Name}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// The key of the row an item was read out of does not leave the gateway that read it.
    /// </summary>
    /// <remarks>
    /// It is the one number a pile does have, and it is a fact about the row rather than about the
    /// pile. Kept inside the gateway it is harmless; handed upwards it is an identity-shaped value
    /// that nothing about a pile justifies, and the projection this server hands the client used to
    /// carry exactly that.
    /// </remarks>
    [Fact]
    public void NothingOutsideTheRowGatewaySaysAnItemsStorageRowKey()
    {
        var violations = Members()
            .Where(member => RowKeyNames.Contains(member.Name, StringComparer.Ordinal))
            // A typed optional world identity is not a storage row key.
            .Where(member => member.Type != typeof(ItemIdentity?))
            .Where(member => !IsGateway(member.Owner))
            // A creature is always one world instance; its item UID is its domain identity.
            .Where(member => !IsWorldInstanceIdentity(member.Owner))
            .Select(member => $"{member.Owner.FullName}.{member.Name}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    // These shapes describe singular world instances and cannot represent an interchangeable pile.
    private static bool IsWorldInstanceIdentity(Type type) =>
        type == typeof(CreatureState)
        || type == typeof(AddPetStomachEffect)
        || type == typeof(RenamePetEffect)
        || type == typeof(IEggInstance) || type == typeof(IPetInstance) || type == typeof(IEquipmentInstance)
        || typeof(IEggInstance).IsAssignableFrom(type)
        || typeof(IPetInstance).IsAssignableFrom(type)
        || typeof(IEquipmentInstance).IsAssignableFrom(type);

    [Fact]
    public void WorldInstanceIdentityPermissionDoesNotAdmitGenericInventoryOrPiles()
    {
        Assert.True(IsWorldInstanceIdentity(typeof(RenamePetEffect)));
        Assert.False(IsWorldInstanceIdentity(typeof(ItemReference)));
        Assert.False(IsWorldInstanceIdentity(typeof(InventoryItemState)));
        Assert.False(IsWorldInstanceIdentity(typeof(ItemPile)));
    }

    /// <summary>What one member of one production type is, for both checks above.</summary>
    private sealed record Member(Type Owner, string Name, Type Type);

    private static IReadOnlyList<Member> Members() =>
    [
        .. Production
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !IsCompilerGenerated(type))
            .SelectMany(Of),
    ];

    private static IEnumerable<Member> Of(Type type)
    {
        foreach (var property in type.GetProperties(
                     BindingFlags.Public | BindingFlags.NonPublic
                     | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return new Member(type, property.Name, property.PropertyType);
        }

        foreach (var field in type.GetFields(
                     BindingFlags.Public | BindingFlags.NonPublic
                     | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (!IsCompilerGenerated(field))
            {
                yield return new Member(type, field.Name, field.FieldType);
            }
        }

        foreach (var parameter in type.GetConstructors(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     .SelectMany(constructor => constructor.GetParameters()))
        {
            yield return new Member(type, parameter.Name ?? string.Empty, parameter.ParameterType);
        }
    }

    /// <summary>Whether this type is one of the two places a row key belongs.</summary>
    private static bool IsGateway(Type type) =>
        RowKeyIsAllowedIn.Keys.Any(allowed =>
            (type.Namespace ?? string.Empty).Equals(allowed, StringComparison.Ordinal)
            || (type.Namespace ?? string.Empty).StartsWith(allowed + ".", StringComparison.Ordinal));

    private static bool IsCompilerGenerated(MemberInfo member) =>
        member.GetCustomAttributes(
            typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Length > 0
        || member.Name.Contains('<', StringComparison.Ordinal);
}
