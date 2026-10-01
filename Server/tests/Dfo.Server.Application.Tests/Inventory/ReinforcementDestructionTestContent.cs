using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Tests.Inventory;

internal sealed class ReinforcementDestructionTestContent(int rarity, int grade = 50, int value = 1000)
    : IEquipmentDisjointDefinitionSource
{
    public ValueTask<EquipmentDestructionDefinition?> GetDestructionEquipmentAsync(int itemId, CancellationToken ct) =>
        ValueTask.FromResult<EquipmentDestructionDefinition?>(itemId == 3000 ? new(value, grade, rarity, 200) : null);
    public ValueTask<EquipmentDisjointDefinition?> GetEquipmentAsync(int itemId, CancellationToken ct) =>
        throw new NotSupportedException("The reinforcement failure path must not call NPC admission.");
    public ValueTask<PortableDisjointMachineDefinition?> GetPortableMachineAsync(int itemId, CancellationToken ct) =>
        throw new NotSupportedException();
    public ValueTask<EquipmentDisjointTable?> GetSystemTableAsync(CancellationToken ct) =>
        ValueTask.FromResult<EquipmentDisjointTable?>(new(3037, 150, [1.12, 1.35, .07, 1.48, 1.8, .7, 1.53],
            [[], [3033, 3034, 3035, 3036], [3166], [3167], [10088692], [3229], [3332]],
            Enumerable.Range(0, 7).Select(_ => new EquipmentDisjointAdditionalRule(10, 10, 0, 100)).ToArray(),
            [new(true, 10100115, 1, 100, 0)]));
}