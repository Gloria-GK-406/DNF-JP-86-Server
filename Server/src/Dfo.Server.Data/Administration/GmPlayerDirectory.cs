using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Administration;

public sealed record GmPlayerFilter(int Page = 1, int PageSize = 20, int? AccountId = null,
    int? CharacterId = null, string? Q = null, bool OnlineOnly = false);
public sealed record GmPage<T>(IReadOnlyList<T> Items, long Total, int Page, int PageSize, DateTimeOffset SampledAt);
public sealed record GmAccountRecord(int AccountId, string LoginName, AccountCeraBalances Balances, int CharacterCount);
public sealed record GmCharacterRecord(int CharacterId, int AccountId, string LoginName, byte[]? NameBytes,
    int Job, int GrowType, int Level, bool IsDeleted, CharacterPositionState SavedPosition, AccountCeraBalances Balances);
public sealed record GmStoredPage<T>(IReadOnlyList<T> Items, long Total);
public sealed record GmAccountView(int AccountId, string LoginName, AccountCeraBalances Balances,
    int CharacterCount, bool IsOnline, int? ActiveCharacterId, DateTimeOffset SampledAt);
public sealed record GmChannelView(int ChannelId, string Name);
public sealed record GmPositionView(string Source, byte? TownId, byte? AreaId, short? X, short? Y, byte? Direction);
public sealed record GmCharacterView(int CharacterId, int AccountId, string LoginName, string Name,
    string NameDecodeStatus, string? NameBytesHex, int Job, int GrowType, int Level, bool IsDeleted,
    bool IsOnline, GmChannelView? Channel, GmPositionView Position, DateTimeOffset SampledAt);
public sealed record GmAccountLookup(GmAccountView? Value);
public sealed record GmCharacterDetail(GmCharacterView Character, AccountCeraBalances Balances);
public sealed record GmCharacterLookup(GmCharacterDetail? Value);
public sealed record GmChannelCount(int ChannelId, string Name, int OnlineCharacters);
public sealed record GmOverview(bool NetworkEnabled, bool GameReady, long TotalAccounts, long TotalCharacters,
    int OnlineAccounts, int OnlineCharacters, IReadOnlyList<GmChannelCount> Channels,
    int UnmappedOnlineCharacters, DateTimeOffset SampledAt);
