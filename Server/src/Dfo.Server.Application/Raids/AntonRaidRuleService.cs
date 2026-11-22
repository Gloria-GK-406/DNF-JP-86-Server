using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Raids;

namespace Dfo.Server.Application.Raids;

internal sealed class AntonRaidRuleService(IAntonRaidContentSource source)
{
    internal AntonRaidRules Current => source.Load();

    internal static bool CanStartWith(AntonRaidRules rules, int members) =>
        members >= rules.MinimumMembers && members <= rules.MaximumMembers;

    internal static int HatcheryAt(int room) => room switch
    {
        19100 => 224,
        19103 => 221,
        19116 => 223,
        19119 => 222,
        _ => 0,
    };
}
