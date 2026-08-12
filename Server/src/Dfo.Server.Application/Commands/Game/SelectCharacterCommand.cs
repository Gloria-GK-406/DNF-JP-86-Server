using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.OnlineUsers;
using System.Runtime.ExceptionServices;

namespace Dfo.Server.Application.Commands.Game;

/// <summary>
/// Enter the game with one of an account's characters.
/// </summary>
/// <param name="Channel">
/// The channel the connection arrived on. It is resolved before dispatch because entry
/// policy — admission, spawn, position persistence and duelling applicability — all follow
/// from it, and a command that had to ask which listener it came from would be reaching
/// back into the transport.
/// </param>
public sealed record SelectCharacterCommand(
    AccountId AccountId,
    ushort SlotIndex,
    ChannelEntryIdentity Channel,
    NetworkConnectionId ConnectionId = default,
    long ExpectedConnectionGeneration = 0) : ICommand<SelectCharacterResult>, IAccountCommand;

public interface ISelectCharacterPreparationService
{
    ValueTask<ActiveCharacterState> PreflightAsync(
        AccountId accountId, ushort slotIndex, ChannelEntryIdentity channel,
        CancellationToken cancellationToken);
    ValueTask<SelectCharacterResult> PrepareAsync(
        ActiveCharacterState selected,
        AccountId accountId,
        ChannelEntryIdentity channel,
        CancellationToken cancellationToken);
}

public sealed class SelectCharacterCommandHandler(
    IOnlineUserQueryService onlineUsers,
    ISelectCharacterPreparationService preparation,
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionCleanupService cleanup,
    ISelectCharacterEntryInventoryService inventoryEntry,
    ISelectCharacterSessionEntryService sessionEntry,
    ISelectCharacterQuestEntryService questEntry,
    ISelectCharacterPetEntryService petEntry,
    IActiveCharacterService activeCharacters,
    ICharacterEntryMutationRepository characterMutations,
    SelectedSessionTransitionGate lifecycleGate,
    INetworkMessageSender sender,
    INetworkConnectionCloser closer)
    : ICommandHandler<SelectCharacterCommand, SelectCharacterResult>
{
    public async ValueTask<SelectCharacterResult> HandleAsync(
        CommandExecutionContext context, SelectCharacterCommand command,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(command.ConnectionId, out var online)
            || online?.AccountId != command.AccountId
            || online.Generation != command.ExpectedConnectionGeneration)
            throw new InvalidOperationException("Select-character connection generation is stale.");

        var pendingCloses = new List<NetworkConnectionId>();
        var lifecycleLease = await lifecycleGate.AcquireAsync(
            command.AccountId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        SelectCharacterResult? completed = null;
        ExceptionDispatchInfo? failure = null;
        try
        {
            completed = await ExecuteUnderLifecycleGateAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            await lifecycleLease.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var connectionId in pendingCloses.Distinct())
        {
            try
            {
                await closer.CloseAsync(connectionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Transport close is best effort after lifecycle ownership is released.
                // It must neither mask the workflow failure nor invalidate a new success.
            }
        }
        failure?.Throw();
        return completed ?? throw new InvalidOperationException("Select-character workflow produced no result.");

        async ValueTask<SelectCharacterResult> ExecuteUnderLifecycleGateAsync()
        {
        if (!onlineUsers.TryGetByConnection(command.ConnectionId, out online)
            || online?.AccountId != command.AccountId
            || online.Generation != command.ExpectedConnectionGeneration)
            throw new InvalidOperationException("Select-character connection generation is stale.");

        ActiveCharacterState selected;
        try
        {
            selected = await preparation.PreflightAsync(
                command.AccountId, command.SlotIndex, command.Channel, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelAdmissionRefusedException refusal)
        {
            await sender.SendProjectionAsync(command.ConnectionId,
                new SelectCharacterProjectionDelivery(
                    [new SelectCharacterAdmissionRejectionProjection(
                        refusal.Refusal.ErrorCode, refusal.Refusal.Message)], []), cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        // Wire representability is a preflight and therefore precedes every lifecycle side effect.
        _ = SelectedSessionIdentity.ToWireUserId(selected.Identity.CharacterId);
        var previous = selectedSessions.Find(command.AccountId);
        if (previous is not null)
            await inventoryEntry.SaveExistingLeaseAsync(previous, cancellationToken).ConfigureAwait(false);
        if (previous?.ConnectionId == command.ConnectionId)
        {
            try
            {
                await cleanup.CleanupAsync(previous, includeOwner: true, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _ = selectedSessions.TryRemoveExact(previous);
                pendingCloses.Add(command.ConnectionId);
                throw;
            }
        }
        if (!onlineUsers.TryGetByConnection(command.ConnectionId, out var refreshed)
            || refreshed?.AccountId != command.AccountId
            || (previous?.ConnectionId != command.ConnectionId
                && refreshed.Generation != command.ExpectedConnectionGeneration))
            throw new InvalidOperationException("Select-character connection changed during cleanup.");
        var session = SelectedSessionIdentity.Create(
            command.AccountId, selected.Identity.CharacterId, command.ConnectionId,
            refreshed.Generation, command.Channel.ListenerPort);

        try
        {
            var displaced = selectedSessions.RegisterReplacing(session);
            if (displaced is not null)
            {
                try
                {
                    await cleanup.CleanupAsync(displaced, includeOwner: false, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _ = selectedSessions.TryRemoveExact(displaced);
                    pendingCloses.Add(displaced.ConnectionId);
                }
            }
            var prepared = await preparation.PrepareAsync(
                selected, command.AccountId, command.Channel, cancellationToken).ConfigureAwait(false);
            EnsureCurrent(session);
            var spawn = prepared.World.Channel.Spawn;
            var resolvedPosition = new CharacterPositionState(
                spawn.TownId, spawn.AreaId, spawn.X, spawn.Y, spawn.Direction);
            prepared = prepared with { Character = prepared.Character with { Position = resolvedPosition } };
            prepared = await inventoryEntry.PrepareLeaseAsync(session, prepared, cancellationToken)
                .ConfigureAwait(false);
            if (prepared.World.Channel.PersistsPosition)
                await characterMutations.PersistPositionAsync(
                    command.AccountId, session.CharacterId, resolvedPosition,
                    cancellationToken).ConfigureAwait(false);

            // The complete G6 static stream is the first required owner delivery.
            EnsureCurrent(session);
            await sender.SendAsync(command.ConnectionId, new SelectCharacterResponse(prepared), cancellationToken)
                .ConfigureAwait(false);
            EnsureCurrent(session);
            var supplemental = BuildPrePublicationProjections(session, prepared);
            await sender.SendProjectionAsync(command.ConnectionId,
                new SelectCharacterProjectionDelivery(supplemental, []), cancellationToken).ConfigureAwait(false);
            EnsureCurrent(session);

            var inventory = await inventoryEntry.ApplyDailyRefillAsync(session, prepared, cancellationToken)
                .ConfigureAwait(false);
            EnsureCurrent(session);
            prepared = inventory.Result;
            if (inventory.CommittedUpdates.Count > 0)
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery(inventory.CommittedUpdates, []), cancellationToken)
                    .ConfigureAwait(false);
            EnsureCurrent(session);

            activeCharacters.Publish(session, prepared.Character);
            sessionEntry.Publish(session, prepared);
            EnsureCurrent(session);

            var pvp = sessionEntry.PlanPvp(session, prepared);
            if (pvp is not null)
            {
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery([pvp], []), cancellationToken).ConfigureAwait(false);
                EnsureCurrent(session);
                var targets = sessionEntry.PlanPvpPublication(session, prepared);
                sessionEntry.MarkPvpReady(session, prepared);
                if (targets.Count > 0)
                    await sender.SendProjectionAsync(command.ConnectionId,
                        new SelectCharacterProjectionDelivery([], targets), cancellationToken).ConfigureAwait(false);
            }

            await sender.SendProjectionAsync(command.ConnectionId,
                new SelectCharacterProjectionDelivery(
                    [new SelectCharacterTrailingEquipmentLocksProjection(prepared)], []), cancellationToken)
                .ConfigureAwait(false);
            EnsureCurrent(session);
            if (await questEntry.SynchronizeAsync(session, prepared, cancellationToken).ConfigureAwait(false) is { } quests)
            {
                EnsureCurrent(session);
                prepared = quests.Result;
                sessionEntry.ReplaceRuntimeExact(session, prepared);
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery([quests.Projection], []), cancellationToken).ConfigureAwait(false);
            }
            EnsureCurrent(session);
            if (await petEntry.ReviveAsync(session, prepared, cancellationToken).ConfigureAwait(false) is { } pet)
            {
                EnsureCurrent(session);
                prepared = pet.Result;
                sessionEntry.ReplaceRuntimeExact(session, prepared);
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery([pet.Projection], []), cancellationToken).ConfigureAwait(false);
            }
            EnsureCurrent(session);
            if (sessionEntry.PlanRejoin(session, prepared) is { } rejoin)
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery([rejoin], []), cancellationToken).ConfigureAwait(false);
            return prepared;
        }
        catch
        {
            try { await cleanup.CleanupAsync(session, includeOwner: true, CancellationToken.None); }
            finally
            {
                _ = selectedSessions.TryRemoveExact(session);
                pendingCloses.Add(session.ConnectionId);
            }
            throw;
        }
        }
    }

    private static IReadOnlyList<ISelectCharacterSupplementalProjection> BuildPrePublicationProjections(
        SelectedSessionIdentity session, SelectCharacterResult result)
    {
        var projections = new List<ISelectCharacterSupplementalProjection>
        {
            new SelectCharacterVisibilityProjection(session.WireUserId,
                checked((byte)result.CharacterEntry.Public.VisibilityBits)),
            new SelectCharacterCloneTitleProjection(result.CharacterEntry.Public.CloneTitleItemId),
            result.RosterProjection ?? throw new InvalidDataException("Roster projection is unavailable."),
            new SelectCharacterHonorProjection(
                checked((uint)Math.Max(0, result.CharacterEntry.Public.Subtype1.Progress1)),
                checked((uint)Math.Max(0, result.CharacterEntry.Public.Subtype1.Progress2))),
        };
        var mirror = result.CharacterEntry.Skills.ProtocolMirror
            ?? throw new InvalidDataException("Skill protocol mirror is unavailable.");
        projections.Add(new SelectCharacterGrowthCapsuleProjection(
            result.Character.Progression.Level, result.Character.Progression.Experience,
            mirror.Page0Sp, mirror.Page1Sp, mirror.Page0Tp, mirror.Page1Tp,
            result.CharacterEntry.Public.Subtype0.PvpRankPoint,
            result.Account.Progression.GrowthCapsuleExperience,
            checked((uint)Math.Max(0, result.CharacterEntry.Public.Subtype1.Progress1)),
            checked((uint)Math.Max(0, result.CharacterEntry.Public.Subtype1.Progress2))));
        return projections;
    }

    private void EnsureCurrent(SelectedSessionIdentity session)
    {
        if (selectedSessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var current)
            || current?.AccountId != session.AccountId)
            throw new InvalidOperationException("Selected session is no longer current.");
    }
}

public sealed class SelectCharacterPreparationService(
    ICharacterService characters,
    ICharacterInventoryEntryService characterInventoryEntries,
    IAccountInventoryEntryService accountInventoryEntries,
    ICharacterEntryStateService characterEntries,
    IAccountEntryStateService accountEntries,
    IEquippedCreatureQuery equippedCreature,
    IWorldContentEntryService worldContent,
    ISkillPointDefinitionSource skillPoints,
    ICharacterPvpSkillProfileRepository pvpSkills,
    IDungeonPersistentEffectRecoveryRepository dungeonEffects,
    IExpertSkillContractDefinitionSource expertContracts,
    IExpertSkillContractReconciliationRepository expertSkillReconciliation,
    ICreatureEquipmentLifetimeSource creatureLifetimes,
    IChannelEntryPolicy channels,
    IEntryPlacementService placement,
    ISessionAdjacentEntryService sessionAdjacent,
    TimeProvider timeProvider) : ISelectCharacterPreparationService
{
    public async ValueTask<ActiveCharacterState> PreflightAsync(
        AccountId accountId, ushort slotIndex, ChannelEntryIdentity channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var character = await characters.LoadForSelectionAsync(
            accountId,
            slotIndex,
            cancellationToken).ConfigureAwait(false);

        // Admission depends only on minimum character facts. Refuse before loading any
        // entry state so the rejected selection leaves the current online state untouched.
        if (channels.Refuse(channel, character.Progression.Level) is { } refusal)
            throw new ChannelAdmissionRefusedException(channel, refusal);

        return character;
    }

    public async ValueTask<SelectCharacterResult> PrepareAsync(
        ActiveCharacterState character, AccountId accountId, ChannelEntryIdentity channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var recovery = await dungeonEffects.RecoverAsync(
            accountId, character.Identity.CharacterId, cancellationToken).ConfigureAwait(false);
        if (recovery.FailedCount > 0 || recovery.RemainingCount > 0)
            throw new InvalidDataException("Dungeon persistent effects were not completely recovered.");
        if (recovery.RequiresReload)
            character = await characters.LoadForSelectionAsync(
                accountId, character.Identity.SlotIndex, cancellationToken).ConfigureAwait(false);

        var roster = await characters.GetAccountRosterAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        if (roster.AccountId != accountId
            || roster.Characters.All(value => value.CharacterId != character.Identity.CharacterId))
        {
            throw new InvalidDataException("Selected character is absent from the account roster.");
        }

        var characterInventory = await characterInventoryEntries.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var accountInventory = await accountInventoryEntries.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var characterEntry = await characterEntries.LoadAsync(
            accountId,
            character.Identity.CharacterId,
            cancellationToken).ConfigureAwait(false);
        var learnedSkillIds = characterEntry.Skills.Pages.SelectMany(value => value.Skills)
            .Select(value => checked((int)value.SkillId)).Distinct().ToArray();
        var reconciliationDefinitions = await skillPoints.GetAsync(
            character.Identity.Job, character.Identity.GrowType, character.Progression.Level,
            learnedSkillIds, cancellationToken).ConfigureAwait(false);
        var freeBaseline = reconciliationDefinitions.FreeGrants.GroupBy(value => value.SkillId)
            .ToDictionary(group => group.Key, group => group.Max(value => value.Level));
        if (await expertSkillReconciliation.ReconcileAsync(
                accountId, character.Identity.CharacterId,
                expertContracts.GetOverSkillPremiumTypes(), reconciliationDefinitions.Skills,
                freeBaseline, cancellationToken).ConfigureAwait(false))
            characterEntry = await characterEntries.LoadAsync(
                accountId, character.Identity.CharacterId, cancellationToken).ConfigureAwait(false);
        var accountEntry = await accountEntries.LoadAsync(
            accountId,
            cancellationToken).ConfigureAwait(false);
        var preparedAt = timeProvider.GetUtcNow();
        accountEntry = await CompleteSupportCharacterProjectionAsync(
            accountId, character, roster, accountEntry,
            characters, characterInventoryEntries, characterEntries,
            creatureLifetimes, preparedAt,
            cancellationToken).ConfigureAwait(false);

        var content = await worldContent.LoadAsync(
            BuildContentSubject(character, characterEntry, characterInventory),
            cancellationToken).ConfigureAwait(false);
        var channelPolicy = await placement.ResolveAsync(
            channel,
            character.Position.TownId,
            cancellationToken).ConfigureAwait(false);
        if (channelPolicy.PvpSkillProfileApplies)
        {
            var initial = await BuildInitialPvpSkillProfileAsync(
                character, characterEntry.Skills, skillPoints, cancellationToken).ConfigureAwait(false);
            characterEntry = characterEntry with
            {
                Skills = MergePvpProfile(characterEntry.Skills,
                    await pvpSkills.LoadOrInitializeAsync(
                        accountId, character.Identity.CharacterId, initial, cancellationToken).ConfigureAwait(false)),
            };
        }
        else
            characterEntry = await CompleteSkillProjectionAsync(
                character, characterEntry, skillPoints, cancellationToken).ConfigureAwait(false);
        var adjacent = await sessionAdjacent.LoadAsync(
            new SessionAdjacentSubject(
                character.Identity.CharacterId,
                accountId,
                channel,
                channelPolicy.Spawn.TownId,
                channelPolicy.Spawn.AreaId),
            cancellationToken).ConfigureAwait(false);

        characterEntry = CompleteCollectionBoxProjection(characterEntry, content, preparedAt);
        var rosterProjection = await BuildRosterProjectionAsync(
            accountId, roster, character.Identity.CharacterId, characterEntry,
            characterEntries, cancellationToken).ConfigureAwait(false);

        var result = new SelectCharacterResult(
            character,
            characterEntry,
            characterInventory,
            accountInventory,
            accountEntry,
            new GameWorldEntryState(preparedAt, content, channelPolicy),
            new CharacterSessionEntryState(
                accountId,
                character.Identity.CharacterId,
                adjacent),
            roster,
            rosterProjection);

        return result;
    }

    private static CharacterSkillsEntryState MergePvpProfile(
        CharacterSkillsEntryState normal, CharacterSkillsEntryState pvp) => normal with
    {
        Pages = pvp.Pages,
        ProtocolMirror = pvp.ProtocolMirror
            ?? throw new InvalidDataException("PvP skill protocol mirror is unavailable."),
    };

    private static async ValueTask<SelectCharacterRosterProjection> BuildRosterProjectionAsync(
        AccountId accountId,
        AccountCharacterRoster roster,
        CharacterId selectedId,
        CharacterEntryState selectedEntry,
        ICharacterEntryStateService entries,
        CancellationToken cancellationToken)
    {
        var characters = new List<SelectCharacterRosterCharacterProjection>(roster.Characters.Count);
        foreach (var summary in roster.Characters.OrderBy(value => value.SlotIndex))
        {
            var entry = summary.CharacterId == selectedId
                ? selectedEntry
                : await entries.LoadAsync(accountId, summary.CharacterId, cancellationToken).ConfigureAwait(false);
            characters.Add(new SelectCharacterRosterCharacterProjection(
                summary.CharacterId, summary.SlotIndex, summary.Name, summary.Job, summary.GrowType,
                summary.Level,
                checked((uint)Math.Max(0, entry.Public.Subtype1.Progress1)),
                checked((uint)Math.Max(0, entry.Public.Subtype1.Progress2)),
                0,
                DecodeAppearances(entry.Public.Appearance.EncodedEntries),
                summary.CloneTitleItemId));
        }
        return new SelectCharacterRosterProjection(
            roster.SlotLimit, roster.SlotLimit, selectedEntry.Public.Subtype1.ManageLevel,
            0, 0, 0, characters);
    }

    internal static IReadOnlyList<SelectCharacterRosterAppearanceProjection> DecodeAppearances(byte[] blob)
    {
        if (blob is null || blob.Length == 0) return [];
        using var stream = new MemoryStream(blob, writable: false);
        using var reader = new BinaryReader(stream);
        var count = reader.ReadByte();
        var appearances = new List<SelectCharacterRosterAppearanceProjection>(count);
        for (var index = 0; index < count; index++)
        {
            if (stream.Length - stream.Position < 23)
                throw new InvalidDataException("Character appearance blob is truncated.");
            appearances.Add(new SelectCharacterRosterAppearanceProjection(
                reader.ReadByte(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadBytes(4),
                reader.ReadByte(), reader.ReadInt32(), reader.ReadUInt32(), reader.ReadByte()));
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Character appearance blob contains trailing bytes.");
        return appearances;
    }

    private static CharacterEntryState CompleteCollectionBoxProjection(
        CharacterEntryState entry,
        WorldContentEntryState content,
        DateTimeOffset preparedAt)
    {
        var definitions = content.CollectionBoxes.ToDictionary(value => value.BoxIndex);
        var boxes = entry.Features.Collections.CollectionBoxes.Select(box =>
        {
            if (!definitions.TryGetValue(box.BoxIndex, out var definition))
                throw new InvalidDataException($"Collection-box definition {box.BoxIndex} was not resolved.");

            uint remainingSeconds;
            byte statusFlags;
            if (definition.MaximumExpiresAt is not { } maximum)
            {
                remainingSeconds = 0;
                statusFlags = 1;
            }
            else if (maximum <= preparedAt)
            {
                remainingSeconds = uint.MaxValue;
                statusFlags = 0;
            }
            else
            {
                remainingSeconds = checked((uint)Math.Min(
                    uint.MaxValue,
                    Math.Floor((maximum - preparedAt).TotalSeconds)));
                statusFlags = 0;
            }

            return box with
            {
                Protocol = new CharacterCollectionBoxProtocolState(remainingSeconds, statusFlags),
            };
        }).ToArray();

        return entry with
        {
            Features = entry.Features with
            {
                Collections = entry.Features.Collections with { CollectionBoxes = boxes },
            },
        };
    }

    private static async ValueTask<CharacterEntryState> CompleteSkillProjectionAsync(
        ActiveCharacterState character,
        CharacterEntryState entry,
        ISkillPointDefinitionSource source,
        CancellationToken cancellationToken)
    {
        var learnedIds = entry.Skills.Pages.SelectMany(value => value.Skills)
            .Select(value => checked((int)value.SkillId)).Distinct().ToArray();
        var definitions = await source.GetAsync(
            character.Identity.Job,
            character.Identity.GrowType,
            character.Progression.Level,
            learnedIds,
            cancellationToken).ConfigureAwait(false);
        var baseline = definitions.FreeGrants.GroupBy(value => value.SkillId)
            .ToDictionary(group => group.Key, group => group.Max(value => value.Level));

        ushort RemainingFor(byte pageIndex, bool tp)
        {
            var spent = 0;
            var page = entry.Skills.Pages.SingleOrDefault(value => value.PageIndex == pageIndex);
            foreach (var learned in page?.Skills ?? [])
            {
                if (!definitions.Skills.TryGetValue(checked((int)learned.SkillId), out var skill))
                    continue; // Legacy treats an absent PVF skill definition as unchargeable.
                baseline.TryGetValue(skill.SkillId, out var freeLevel);
                if (skill.IsFixedLevel && character.Progression.Level >= skill.RequiredLevel)
                {
                    var fixedLevel = skill.FixedLevelBase
                        + (character.Progression.Level - skill.RequiredLevel) / Math.Max(1, skill.FixedLevelInterval)
                        * skill.FixedLevelAddPerInterval;
                    freeLevel = checked((byte)Math.Min(byte.MaxValue,
                        Math.Max(freeLevel, Math.Min(fixedLevel, skill.MaximumLevel))));
                }
                if (skill.UsesTp != tp || learned.Level <= freeLevel || skill.Costs.Count == 0)
                    continue;
                for (var level = freeLevel; level < learned.Level; level++)
                    spent = checked(spent + skill.Costs[Math.Min(level, skill.Costs.Count - 1)]);
            }
            var total = (tp ? definitions.TotalTp : definitions.TotalSp)
                + (tp ? entry.Skills.SkillPointBasis.BonusTp : entry.Skills.SkillPointBasis.BonusSp);
            return checked((ushort)Math.Clamp(total - spent, 0, ushort.MaxValue));
        }

        var mirror = new CharacterSkillProtocolMirrorState(
            RemainingFor(0, false), RemainingFor(1, false),
            RemainingFor(0, true), RemainingFor(1, true));
        return entry with { Skills = entry.Skills with { ProtocolMirror = mirror } };
    }

    internal static async ValueTask<CharacterSkillsEntryState> BuildInitialPvpSkillProfileAsync(
        ActiveCharacterState character, CharacterSkillsEntryState basis,
        ISkillPointDefinitionSource source, CancellationToken cancellationToken)
    {
        var definitions = await source.GetAsync(character.Identity.Job, character.Identity.GrowType,
            character.Progression.Level, [], cancellationToken).ConfigureAwait(false);
        var grants = definitions.FreeGrants.GroupBy(value => value.SkillId)
            .Select(group => group.OrderByDescending(value => value.Level).First()).ToArray();
        var occupied = new HashSet<int>();
        var skills = new List<CharacterSkillState>();
        foreach (var grant in grants.OrderBy(value => value.IsInitial ? 0 : 1))
        {
            definitions.Skills.TryGetValue(grant.SkillId, out var definition);
            var active = definition?.IsActive == true;
            var grantLevel = grant.Level;
            if (definition?.IsFixedLevel == true && character.Progression.Level >= definition.RequiredLevel)
            {
                var fixedLevel = definition.FixedLevelBase
                    + (character.Progression.Level - definition.RequiredLevel)
                    / Math.Max(1, definition.FixedLevelInterval) * definition.FixedLevelAddPerInterval;
                grantLevel = checked((byte)Math.Clamp(
                    Math.Max(grantLevel, Math.Min(fixedLevel, definition.MaximumLevel)), 0, byte.MaxValue));
            }
            int slot;
            if (grant.IsInitial)
                slot = FirstFree(active ? 0 : 102, active ? 102 : 204);
            else
            {
                var group = ReformGroup(definition?.RawGroup ?? 0, active, definition?.NumberOfGrowTypes ?? 0);
                slot = active && character.Identity.Job != 9
                    ? FirstFree(0, 6) is var primary && primary >= 0 ? primary : FirstFree(198, 204)
                    : FirstFree(new[] { 6, 54, 102, 150 }[Math.Clamp(group, 0, 3)],
                        new[] { 54, 102, 150, 198 }[Math.Clamp(group, 0, 3)]);
            }
            if (slot < 0) slot = FirstFree(0, 204);
            if (slot < 0) throw new InvalidDataException("PvP initial skill profile has no free slot.");
            occupied.Add(slot);
            skills.Add(new CharacterSkillState(checked((short)slot), checked((ushort)grant.SkillId), grantLevel, []));
        }
        return new CharacterSkillsEntryState(
            [new CharacterSkillPageState(0, skills), new CharacterSkillPageState(1, skills)], [],
            basis.SkillPointBasis, basis.DarkKnightCombos, basis.KnightShieldDeck,
            new CharacterSkillProtocolMirrorState(
                ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue));

        int FirstFree(int start, int end)
        { for (var index = start; index < end; index++) if (!occupied.Contains(index)) return index; return -1; }
        static int ReformGroup(int raw, bool active, int grows) => active ? 3
            : raw is >= 0 and <= 3 ? grows <= 2 ? 1 : 0 : raw == 4 ? 2 : raw;
    }

    private static async ValueTask<AccountEntryState> CompleteSupportCharacterProjectionAsync(
        AccountId accountId,
        ActiveCharacterState selected,
        AccountCharacterRoster roster,
        AccountEntryState account,
        ICharacterService characters,
        ICharacterInventoryEntryService inventories,
        ICharacterEntryStateService entries,
        ICreatureEquipmentLifetimeSource creatureLifetimes,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        var ownerRelations = account.Relations.CharacterRelations
            .Where(value => value.OwnerCharacterId == selected.Identity.CharacterId).ToArray();
        if (ownerRelations.Length == 0)
            return account;
        if (ownerRelations.Length != 1 || ownerRelations[0].Slot != 0)
            throw new InvalidDataException("Select-character supports exactly one owner relation in slot zero.");
        var relation = ownerRelations[0];
        var summary = roster.Characters.SingleOrDefault(
            value => value.CharacterId == relation.SupportCharacterId)
            ?? throw new InvalidDataException("Support character is absent from the account roster.");
        var support = await characters.LoadForSelectionAsync(
            accountId, summary.SlotIndex, cancellationToken).ConfigureAwait(false);
        var supportEntry = await entries.LoadAsync(
            accountId, relation.SupportCharacterId, cancellationToken).ConfigureAwait(false);
        var supportInventory = await inventories.LoadAsync(
            accountId, relation.SupportCharacterId, cancellationToken).ConfigureAwait(false);
        supportInventory = await CompleteSupportCreatureMarkerAsync(
            supportInventory, creatureLifetimes, preparedAt, cancellationToken)
            .ConfigureAwait(false);
        var completed = relation with
        {
            SupportProjection = new AccountSupportCharacterEntryState(
                support, supportEntry, supportInventory),
        };
        return account with
        {
            Relations = account.Relations with
            {
                CharacterRelations = account.Relations.CharacterRelations
                    .Select(value => value == relation ? completed : value).ToArray(),
            },
        };
    }

    private static async ValueTask<CharacterInventoryEntryState> CompleteSupportCreatureMarkerAsync(
        CharacterInventoryEntryState inventory,
        ICreatureEquipmentLifetimeSource lifetimes,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        var containers = new List<InventoryContainerState>(inventory.Containers.Count);
        foreach (var container in inventory.Containers)
        {
            if (container.ContainerType != InventoryContainerType.Equipment)
            {
                containers.Add(container);
                continue;
            }

            var items = new List<InventoryItemState>(container.Items.Count);
            foreach (var item in container.Items)
            {
                if (item.SlotIndex != 24 || item.Core.ItemKind != 5)
                {
                    items.Add(item);
                    continue;
                }

                var lifetime = await lifetimes.GetAsync(
                    item.Core.ItemId, preparedAt, cancellationToken).ConfigureAwait(false);
                var marker = SupportCreatureMarkerProjection.Resolve(
                    lifetime, item.Core.ExpireTime, item.Core.Marker16, preparedAt);
                items.Add(item with { Noti2CreatureMarker = marker });
            }
            containers.Add(container with { Items = items });
        }
        return inventory with { Containers = containers };
    }

    private WorldContentSubject BuildContentSubject(
        ActiveCharacterState character,
        CharacterEntryState entry,
        CharacterInventoryEntryState characterInventory)
    {
        var claimedGroups = entry.Progression.DailyChallenges
            .Where(group => group.RewardClaimed)
            .Select(group => checked((int)group.GroupId))
            .ToArray();
        var boxes = entry.Features.Collections.CollectionBoxes
            .Select(box => new CollectionBoxProgressSubject(
                box.BoxIndex,
                box.Slots.Select(slot => slot.ItemId).ToArray()))
            .ToArray();
        var clearedQuestFlags = entry.Quests.VisibilityFlags
            .Select((value, questId) => (questId, value))
            .Where(pair => pair.value != 0)
            .ToDictionary(pair => pair.questId, pair => checked((int)pair.value));

        return new WorldContentSubject(
            character.Progression.Level,
            character.Identity.Job,
            character.Identity.GrowType,
            clearedQuestFlags,
            claimedGroups,
            boxes,
            equippedCreature.GetItemId(characterInventory),
            entry.Public.Subtype1.EquippedCreatureLevel);
    }
}
