using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Parties;

public sealed class PartyMemberStatusDomainServiceTests
{
    [Fact]
    public async Task Invite_and_realtime_values_come_from_their_current_domain_sources()
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(3),
            new CharacterId(1),
            new NetworkConnectionId("Game:party-status"),
            connectionGeneration: 1,
            listenerPort: 10011);
        var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(
            session.AccountId, session.CharacterId.Value, slotIndex: 0);
        var inventory = result.CharacterInventory with
        {
            VirtualResources = [new InventoryRuntimeValueState(1, 1, 64)],
        };
        result = result with
        {
            CharacterInventory = inventory,
            CharacterEntry = result.CharacterEntry with
            {
                Public = result.CharacterEntry.Public with
                {
                    DerivedStats = result.CharacterEntry.Public.DerivedStats with
                    {
                        CurrentHpMpPercent = 73,
                    },
                },
            },
        };
        var runtime = new SelectedSessionRuntimeDirectory();
        runtime.Publish(session, result, shouldPersistPosition: false);
        var sessions = new SelectedSessionDomainService();
        sessions.Select(session);
        var service = new PartyMemberStatusDomainService(
            () => new FixedInventory(inventory),
            new FixedFatigue(new CharacterFatigueBalance(188, 31)),
            sessions,
            runtime);

        var invitation = await service.InvitationOfAsync(
            Character(session), CancellationToken.None);

        Assert.Equal(new PartyInviteDisplayState(64, 157, 0, 0), invitation);
        Assert.Equal(73, service.CurrentHpPercentOf(session.CharacterId));
    }

    [Fact]
    public async Task Missing_values_stay_zero_and_hp_is_bounded_for_the_wire()
    {
        var session = SelectedSessionIdentity.Create(
            new AccountId(3),
            new CharacterId(1),
            new NetworkConnectionId("Game:party-status"),
            connectionGeneration: 1,
            listenerPort: 10011);
        var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(
            session.AccountId, session.CharacterId.Value, slotIndex: 0);
        result = result with
        {
            CharacterEntry = result.CharacterEntry with
            {
                Public = result.CharacterEntry.Public with
                {
                    DerivedStats = result.CharacterEntry.Public.DerivedStats with
                    {
                        CurrentHpMpPercent = 137,
                    },
                },
            },
        };
        var runtime = new SelectedSessionRuntimeDirectory();
        runtime.Publish(session, result, shouldPersistPosition: false);
        var sessions = new SelectedSessionDomainService();
        sessions.Select(session);
        var service = new PartyMemberStatusDomainService(
            () => new FixedInventory(result.CharacterInventory with { VirtualResources = [] }),
            new FixedFatigue(new CharacterFatigueBalance(0, 0)),
            sessions,
            runtime);

        Assert.Equal(new PartyInviteDisplayState(0, 0, 0, 0),
            await service.InvitationOfAsync(Character(session), CancellationToken.None));
        Assert.Equal(100, service.CurrentHpPercentOf(session.CharacterId));

        Assert.True(runtime.RemoveExact(session));
        Assert.Equal(0, service.CurrentHpPercentOf(session.CharacterId));
    }

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, null);

    private sealed class FixedInventory(CharacterInventoryEntryState state)
        : ICharacterInventoryEntryDomainService
    {
        public ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(state);
    }

    private sealed class FixedFatigue(CharacterFatigueBalance balance)
        : ICharacterFatigueDomainService
    {
        public ValueTask<CharacterFatigueBalance> BalanceOfAsync(
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(balance);

        public ValueTask SpendAsync(
            CharacterId characterId,
            ushort points,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefundAsync(
            CharacterId characterId,
            ushort points,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
