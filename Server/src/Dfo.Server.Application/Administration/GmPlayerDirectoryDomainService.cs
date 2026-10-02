using System.Text;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

internal sealed class GmPlayerDirectoryDomainService(IGmPlayerDirectoryPersistence persistence,
    GmPlayerRuntimeQuery runtime, IGameServerReadiness readiness) : IGmPlayerDirectoryDomainService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public async ValueTask<GmPage<GmAccountView>> AccountsAsync(GmPlayerFilter filter, CancellationToken token)
    {
        var sample = await runtime.SampleAsync(token);
        var records = await persistence.AccountsAsync(filter, filter.OnlineOnly ? sample.Accounts : null, token);
        return new(records.Items.Select(record => Account(record, sample)).ToArray(), records.Total, filter.Page, filter.PageSize, sample.SampledAt);
    }
    public async ValueTask<GmPage<GmCharacterView>> CharactersAsync(GmPlayerFilter filter, CancellationToken token)
    {
        var sample = await runtime.SampleAsync(token);
        var records = await persistence.CharactersAsync(filter, filter.OnlineOnly ? sample.Characters.Keys.ToArray() : null, token);
        return new(records.Items.Select(record => Character(record, sample)).ToArray(), records.Total, filter.Page, filter.PageSize, sample.SampledAt);
    }
    public async ValueTask<GmAccountLookup> AccountAsync(int id, CancellationToken token) =>
        new((await AccountsAsync(new GmPlayerFilter(AccountId: id), token)).Items.FirstOrDefault());
    public async ValueTask<GmCharacterLookup> CharacterAsync(int id, CancellationToken token)
    {
        var sample = await runtime.SampleAsync(token);
        var records = await persistence.CharactersAsync(new GmPlayerFilter(CharacterId: id), null, token);
        var record = records.Items.FirstOrDefault();
        return new(record is null ? null : new(Character(record, sample), record.Balances));
    }
    public async ValueTask<GmOverview> OverviewAsync(CancellationToken token)
    {
        var sample = await runtime.SampleAsync(token);
        var counts = await persistence.CountsAsync(token);
        return new(readiness.NetworkEnabled, readiness.IsReady, counts.Accounts, counts.Characters, sample.Accounts.Count,
            sample.Characters.Count, sample.Channels.Select(channel => new GmChannelCount(channel.ChannelId, channel.Name,
                sample.Characters.Values.Count(character => character.Channel?.ChannelId == channel.ChannelId))).ToArray(),
            sample.Characters.Values.Count(character => character.Channel is null), sample.SampledAt);
    }
    private static GmAccountView Account(GmAccountRecord record, GmRuntimeSample sample) =>
        new(record.AccountId, record.LoginName, record.Balances, record.CharacterCount, sample.Accounts.Contains(record.AccountId),
            sample.Characters.Where(pair => pair.Value.AccountId == record.AccountId).Select(pair => (int?)pair.Key).FirstOrDefault(), sample.SampledAt);

    private static GmCharacterView Character(GmCharacterRecord record, GmRuntimeSample sample)
    {
        var (name, status, hex) = Name(record.NameBytes);
        var isOnline = sample.Characters.TryGetValue(record.CharacterId, out var playing) && playing.AccountId == record.AccountId;
        return new(record.CharacterId, record.AccountId, record.LoginName, name, status, hex, record.Job, record.GrowType,
            record.Level, record.IsDeleted, isOnline, isOnline ? playing!.Channel : null,
            isOnline ? playing!.Position : GmPlayerRuntimeQuery.Position("saved", record.SavedPosition), sample.SampledAt);
    }
    private static (string Name, string Status, string? Hex) Name(byte[]? bytes)
    {
        if (bytes is null) return ("姓名字节缺失", "missing", null);
        try { return (StrictUtf8.GetString(bytes), "valid", null); }
        catch (DecoderFallbackException)
        {
            var hex = Convert.ToHexString(bytes);
            return ($"姓名编码异常（hex:{hex}）", "invalid", hex);
        }
    }
}
