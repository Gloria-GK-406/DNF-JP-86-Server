using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Accounts;

internal sealed class ClientSettingsDomainService(IClientSettingsTable table,
    IOnlineUserQueryDomainService users, ISelectedSessionDomainService sessions,
    IDomainTransactionAccessor transactions, ICharacterSettingsDomainService characterSettings,
    IEventService events) : IClientSettingsDomainService
{
    private sealed record Profile(ClientSettingsOrigin Origin, byte KeyType, Guid Token);
    private readonly ConcurrentDictionary<NetworkConnectionId, Profile> profiles = new();

    public async ValueTask<bool> SaveMainAsync(ClientSettingsOrigin origin, byte[] payload, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(ClientSettingsDomainService) + "." + nameof(SaveMainAsync));
        DemandTransaction();
        if (payload.Length != 150 || !IsCurrent(origin)) return false;
        ClientAppearanceOptions? previous = null;
        byte fallback = 0;
        if (origin.SelectedSession is { } selected)
        {
            fallback = checked((byte)((await characterSettings.FindAsync(selected.CharacterId, ct).ConfigureAwait(false))?.VisibilityBits ?? 0));
            previous = await ReadAppearanceAsync(origin.AccountId, fallback, ct).ConfigureAwait(false);
        }
        await table.SaveMainAsync(origin.AccountId, [.. payload], ct);
        if (previous is not null && previous.VisibilityBits != new ClientAppearanceOptions(payload, fallback).VisibilityBits)
            _ = await events.EnqueueAsync(new ClientAppearanceSettingsChanged(origin.AccountId, origin.SelectedSession!.CharacterId), ct).ConfigureAwait(false);
        return true;
    }

    public async ValueTask<ClientAppearanceOptions> ReadAppearanceAsync(AccountId accountId, byte fallbackVisibility, CancellationToken ct) =>
        new(await table.ReadMainAsync(accountId, ct).ConfigureAwait(false), fallbackVisibility);

    public async ValueTask<bool> SaveQuickChatAsync(ClientSettingsOrigin origin, byte bank, byte[] payload, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(ClientSettingsDomainService) + "." + nameof(SaveQuickChatAsync));
        DemandTransaction();
        if (bank > 1 || payload.Length != 300 || !IsCurrent(origin)) return false;
        await table.SaveQuickChatAsync(origin.AccountId, bank, [.. payload], ct);
        return true;
    }

    public async ValueTask<bool> SaveHotkeysAsync(ClientHotkeyUploadContext context, byte[] payload, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(ClientSettingsDomainService) + "." + nameof(SaveHotkeysAsync));
        DemandTransaction();
        if (payload.Length > 65536 || !IsCurrent(context.Origin)
            || !profiles.TryGetValue(context.Origin.Connection.ConnectionId, out var profile)
            || profile.Token != context.ProfileToken || profile.KeyType != context.KeyType) return false;
        await table.SaveHotkeysAsync(context.Origin.AccountId, context.KeyType, [.. payload], ct);
        return true;
    }

    public ValueTask<IReadOnlyList<HotkeyProfileState>> ReadHotkeysAsync(AccountId accountId, CancellationToken ct) =>
        table.ReadHotkeysAsync(accountId, ct);

    public bool ActivateHotkeyProfile(ClientSettingsOrigin origin, byte keyType)
    {
        GameplayWriteAssertion.Writing(nameof(ClientSettingsDomainService) + "." + nameof(ActivateHotkeyProfile));
        if (!IsCurrent(origin)) return false;
        profiles[origin.Connection.ConnectionId] = new(origin, keyType, Guid.NewGuid());
        return true;
    }

    public ClientHotkeyUploadContext? CaptureHotkeyUpload(ClientSettingsOrigin origin) =>
        IsCurrent(origin) && profiles.TryGetValue(origin.Connection.ConnectionId, out var profile)
        && profile.Origin.Connection == origin.Connection && profile.Origin.AccountId == origin.AccountId
        // Returning to the roster does not send 1C7: the last delivered profile remains
        // active there. A new non-null selection must explicitly activate its own profile.
        && (profile.Origin.SelectedSession == origin.SelectedSession || origin.SelectedSession is null)
            ? new(origin, profile.KeyType, profile.Token) : null;

    public void ForgetConnection(NetworkConnectionId connectionId)
    {
        GameplayWriteAssertion.Writing(nameof(ClientSettingsDomainService) + "." + nameof(ForgetConnection));
        profiles.TryRemove(connectionId, out _);
    }

    public void ForgetConnection(GameSessionIdentity connection)
    {
        GameplayWriteAssertion.Writing(nameof(ClientSettingsDomainService) + "." + nameof(ForgetConnection));
        if (profiles.TryGetValue(connection.ConnectionId, out var profile) && profile.Origin.Connection == connection)
            ((ICollection<KeyValuePair<NetworkConnectionId, Profile>>)profiles).Remove(new(connection.ConnectionId, profile));
    }

    private bool IsCurrent(ClientSettingsOrigin origin) =>
        users.TryGetByConnection(origin.Connection.ConnectionId, out var user)
        && user is not null && user.SessionIdentity == origin.Connection
        && user.AccountId == origin.AccountId && user.Generation == origin.Generation
        && (origin.SelectedSession is { } session
            ? session.ConnectionId == origin.Connection.ConnectionId && sessions.IsSelected(session)
            : sessions.FindByConnection(origin.Connection.ConnectionId) is null)
        && (origin.SelectedSession is not { } selected || selected.AccountId == origin.AccountId);

    private void DemandTransaction()
    {
        if (transactions.Current is null) throw new InvalidOperationException("Saving client settings requires a transaction.");
    }
}
