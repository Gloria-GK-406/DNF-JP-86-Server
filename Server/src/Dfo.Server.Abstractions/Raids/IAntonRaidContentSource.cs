using Dfo.Server.Data.Raids;

namespace Dfo.Server.Abstractions.Raids;

public interface IAntonRaidContentSource
{
    AntonRaidRules Load();
}
