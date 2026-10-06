using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Tests;

// Detached observations for pure rule tests; storage mutations use real lifecycle sessions.
internal static class TestItemReferences
{
    public static ItemReference FromCore(int slot, InventoryItemCoreState core, ItemWorldClass world) =>
        new(new object(), new("rule-observation"), slot, 0,
            ItemSnapshot.FromCore(core, world == ItemWorldClass.Interchangeable,
                world == ItemWorldClass.Interchangeable ? null : new ItemIdentity(core.Value)));
}
