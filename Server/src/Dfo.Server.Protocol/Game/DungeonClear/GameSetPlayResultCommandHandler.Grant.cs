using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Effects;
namespace Dfo.Server.Protocol.Game.DungeonClear;
internal sealed partial class GameSetPlayResultCommandHandler
{
    private ValueTask<ClearExperienceResult> GrantAsync(CommandExecutionContext execution,
        SelectedSessionRecipient actor, DungeonRunState run, ClearCircumstances clear, int settlementLevel, CancellationToken ct) =>
        units.WriteAsync(execution, new("grant-clear-experience", [actor], Footprint: CommandFootprintMode.NonRequest),
            (unit, token) => settlements.GrantAsync(actor.Session.AccountId, actor.Session.CharacterId, run.Layout.DungeonId, clear, actor.Session, run.RunId.Value, settlementLevel, null, unit.Footprint, token), ct);
}
