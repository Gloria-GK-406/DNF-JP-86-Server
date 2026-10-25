using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.DungeonEntry;
internal partial class GameSelectDungeonCommandHandler
{
    protected async ValueTask SelectDungeonAsync(
        CommandExecutionContext execution,
        NetworkDispatchContext context,
        SelectDungeonRequest message,
        CancellationToken cancellationToken)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var result = session is null ? null
            : await currentViews.TryLoadAsync(session, cancellationToken).ConfigureAwait(false);
        if (session is null || result is null)
        {
            logger.LogWarning(
                "Select dungeon {ConnectionId}: no published session, ignoring dungeon {DungeonId}",
                context.Connection.ConnectionId,
                message.DungeonId);
            return;
        }

        if (IllusionTowerProjectionDelivery.Stale(message.Metadata))
        {
            logger.LogWarning(
                "Select dungeon {ConnectionId}: stale inbound sequence ignored before consuming intent",
                context.Connection.ConnectionId);
            return;
        }

        var intent = entryIntents.Consume(session);
        if (intent is null
            || (intent.Value.Kind is DungeonEntryIntentKind.Directed or DungeonEntryIntentKind.Cycle or DungeonEntryIntentKind.OrdinaryRechallenge
                && intent.Value.DirectedDungeonId != message.DungeonId && message.MirrorQuestId is null
                && !(intent.Value.Kind == DungeonEntryIntentKind.Directed
                    && intent.Value.DirectedDungeonId is { } prior && sao?.Supports(prior) == true
                    && sao.Supports(message.DungeonId))))
        {
            if (intent is { } refusedIntent && sessions.IsInWorld(session))
                entryIntents.RestoreIfAbsent(session, refusedIntent);
            logger.LogWarning(
                "Select dungeon {ConnectionId}: invalid selection context intent={Intent} dungeon={DungeonId}",
                context.Connection.ConnectionId,
                intent,
                message.DungeonId);
            await register(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new SelectDungeonRefusal(SelectDungeonRefusal.InvalidSelectionState, 0, message.MirrorQuestId is not null)], []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // Gathered here and handed over, so the rule stays a function of what it is given. They
        // are asked of the character's own log rather than read out of the session, because the
        // session's copy is whatever the entry batch put there and nothing refreshes it: a quest
        // taken on or handed in since the character entered the world would leave the maze chosen
        // against the state the player has already moved on from. Which quests they are is still
        // decided once, here, and the layout stays fixed to the run it opens.
        var questLog = questLogs.LogOf(session.AccountId, session.CharacterId);
        var carried = await questLog.CarriedAsync(cancellationToken).ConfigureAwait(false);
        var cleared = await questLog.ClearedAsync(cancellationToken).ConfigureAwait(false);
        var facts = new DungeonEntryFacts(
            message.Difficulty,
            carried.Select(quest => quest.QuestId).ToArray(),
            cleared.Select(quest => quest.QuestId).ToArray(),
            intent.Value);

        // The entry intent decides how the selected dungeon is validated and resolved; it does
        // not decide whether the character belongs to a party. The selection-screen handler has
        // already moved the whole party into one selection context for both interactive gates and
        // directed story entries. Resolve the same party here so the final confirmation creates
        // one shared run instead of silently turning an interactive party into parallel solo runs.
        var roster = PartyService!.FindParty(new PartyCharacterSnapshot(
            session.AccountId, session.CharacterId, session.WireUserId, null));
        if (roster.LeaderWireUserId != session.WireUserId)
        {
            if (sessions.IsInWorld(session))
                entryIntents.RestoreIfAbsent(session, intent.Value);
            await RefuseAsync(
                context.Connection.ConnectionId,
                SelectDungeonRefusal.NotPartyLeader,
                context: 0,
                cancellationToken, message.MirrorQuestId is not null).ConfigureAwait(false);
            return;
        }

        if (bossTowers?.Supports(message.DungeonId) == true)
        {
            if (message.MirrorQuestId is not null)
            {
                entryIntents.RestoreIfAbsent(session, intent.Value);
                await RefuseAsync(context.Connection.ConnectionId, SelectDungeonRefusal.InvalidSelectionState, 0, cancellationToken, true).ConfigureAwait(false);
                return;
            }
            _ = await EnterBossAsync(execution, context.Connection.ConnectionId, session, roster, intent.Value,
                () => { if (sessions.IsInWorld(session)) entryIntents.RestoreIfAbsent(session, intent.Value); },
                cancellationToken, message.RequestMode, (byte)message.Difficulty, message.PracticeMode).ConfigureAwait(false);
            return;
        }

        // The legacy tower shortcut accepts ordinary entry only. Manual hell must reach the
        // CMD decision/hell admission path, where towers have no valid hell declaration.
        if (message.MirrorQuestId is null && message.RequestMode != 1 && illusionTowers is not null
            && await illusionTowers.SupportsDungeonAsync(message.DungeonId, cancellationToken).ConfigureAwait(false))
        {
            _ = await EnterIllusionAsync(execution,
                context.Connection.ConnectionId,
                session,
                roster,
                intent.Value,
                () =>
                {
                    if (sessions.IsInWorld(session))
                        entryIntents.RestoreIfAbsent(session, intent.Value);
                },
                cancellationToken, message.DungeonId).ConfigureAwait(false);
            return;
        }

        await EnterOrdinaryAsync(execution, context.Connection.ConnectionId, session, result, roster, message, facts, intent.Value, cancellationToken).ConfigureAwait(false);
    }


}
