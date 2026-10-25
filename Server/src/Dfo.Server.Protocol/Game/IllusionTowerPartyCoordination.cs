using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game;
public sealed record IllusionTowerPartyAttempt(
    bool Handled,
    PartyOperationResult Result,
    SelectedSessionIdentity? Target = null);
