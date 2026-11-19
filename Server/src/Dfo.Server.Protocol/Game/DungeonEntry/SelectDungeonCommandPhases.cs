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
internal sealed partial class GameSelectDungeonCommandHandler
{
    internal async ValueTask SelectDungeonAsync(
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

        if (GameRequestFreshness.IsStale(message.Metadata))
        {
            logger.LogWarning(
                "Select dungeon {ConnectionId}: stale inbound sequence ignored before consuming intent",
                context.Connection.ConnectionId);
            return;
        }

        // Only looked at here: a refusal leaves the selection as it was, and the intent is spent
        // once the entry has committed.
        var raidChannel = antonRaid is not null && await antonRaid.IsRaidChannelAsync(session, cancellationToken).ConfigureAwait(false);
        var intent = entryIntents.Authorize(session, message.DungeonId, message.MirrorQuestId is not null);
        if (raidChannel && message.MirrorQuestId is null)
            intent ??= antonRaid?.HatcherySelection(session, message.DungeonId);
        if (intent is null || raidChannel != (intent.Value.Kind == DungeonEntryIntentKind.AntonRaid))
        {
            logger.LogWarning(
                "Select dungeon {ConnectionId}: invalid selection context intent={Intent} dungeon={DungeonId}",
                context.Connection.ConnectionId,
                entryIntents.Find(session),
                message.DungeonId);
            await outbox.RegisterExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new SelectDungeonRefusal(SelectDungeonRefusal.InvalidSelectionState, 0, message.MirrorQuestId is not null)], []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        // The entry intent decides how the selected dungeon is validated and resolved; it does
        // not decide whether the character belongs to a party. The selection-screen handler has
        // already moved the whole party into one selection context for both interactive gates and
        // directed story entries. Resolve the same party here so the final confirmation creates
        // one shared run instead of silently turning an interactive party into parallel solo runs.
        var roster = parties.FindParty(new PartyCharacterSnapshot(
            session.AccountId, session.CharacterId, session.WireUserId, null));
        // Gathered here and handed over, so the rule stays a function of what it is given. They
        // are asked of the characters' own logs rather than read out of the session, because the
        // session's copy is whatever the entry batch put there and nothing refreshes it: a quest
        // taken on or handed in since the character entered the world would leave the maze chosen
        // against the state the player has already moved on from. Which quests they are is still
        // decided once, here, and the layout stays fixed to the run it opens.
        // 70 CheckQuestConnection @ 0x085B6478 accepts a quest maze when any valid member of the
        // party is doing (or has cleared) the quest, so every member's log is read.
        var carriedIds = new HashSet<int>();
        var clearedIds = new HashSet<int>();
        foreach (var member in roster.Members)
        {
            var questLog = questLogs.LogOf(member.Character.AccountId, member.CharacterId);
            carriedIds.UnionWith((await questLog.CarriedAsync(cancellationToken).ConfigureAwait(false)).Select(quest => quest.QuestId));
            clearedIds.UnionWith((await questLog.ClearedAsync(cancellationToken).ConfigureAwait(false)).Select(quest => quest.QuestId));
        }
        var facts = new DungeonEntryFacts(
            message.Difficulty,
            carriedIds.Order().ToArray(),
            clearedIds.Order().ToArray(),
            intent.Value) { PartyMemberCount = roster.Members.Length };

        if (roster.LeaderWireUserId != session.WireUserId)
        {
            await RefuseAsync(
                context.Connection.ConnectionId,
                SelectDungeonRefusal.NotPartyLeader,
                context: 0,
                cancellationToken, message.MirrorQuestId is not null).ConfigureAwait(false);
            return;
        }

        if (!raidChannel && bossTowers.Supports(message.DungeonId) == true)
        {
            if (message.MirrorQuestId is not null)
            {
                await RefuseAsync(context.Connection.ConnectionId, SelectDungeonRefusal.InvalidSelectionState, 0, cancellationToken, true).ConfigureAwait(false);
                return;
            }
            _ = await EnterBossAsync(execution, context.Connection.ConnectionId, session, roster,
                cancellationToken, message.RequestMode, (byte)message.Difficulty, message.PracticeMode).ConfigureAwait(false);
            return;
        }

        // The legacy tower shortcut accepts ordinary entry only. Manual hell must reach the
        // CMD decision/hell admission path, where towers have no valid hell declaration.
        if (!raidChannel && message.MirrorQuestId is null && message.RequestMode != 1
            && await illusionTowers.SupportsDungeonAsync(message.DungeonId, cancellationToken).ConfigureAwait(false))
        {
            _ = await EnterIllusionAsync(execution,
                context.Connection.ConnectionId,
                session,
                roster,
                cancellationToken, message.DungeonId).ConfigureAwait(false);
            return;
        }

        await EnterOrdinaryAsync(execution, context.Connection.ConnectionId, session, result, roster, message, facts, raidChannel,
            cancellationToken).ConfigureAwait(false);
    }


}
