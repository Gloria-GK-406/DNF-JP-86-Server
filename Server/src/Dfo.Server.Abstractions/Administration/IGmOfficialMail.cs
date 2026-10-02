using Dfo.Server.Data.Administration;

namespace Dfo.Server.Abstractions.Administration;

public interface IGmMailPersistence
{
    ValueTask AcquireWriteAsync(CancellationToken token);
    ValueTask<GmMailDocument?> DraftAsync(long administrator, CancellationToken token);
    ValueTask SaveDraftAsync(long administrator, GmMailDocument value, CancellationToken token);
    ValueTask<IReadOnlyList<GmMailDocument>> TemplatesAsync(long administrator, CancellationToken token);
    ValueTask SaveTemplateAsync(long administrator, GmMailDocument value, CancellationToken token);
    ValueTask DeleteTemplateAsync(long administrator, Guid id, CancellationToken token);
    ValueTask<GmStoredPage<GmMailRecipientRecord>> RecipientsAsync(GmPlayerFilter filter, CancellationToken token);
    ValueTask<GmMailOperation?> OperationAsync(long administrator, Guid id, CancellationToken token);
    ValueTask<GmPage<GmMailReceipt>> OperationsAsync(long administrator, int page, int size, CancellationToken token);
    ValueTask SaveOperationAsync(long administrator, GmMailOperation operation, CancellationToken token);
    ValueTask AssertMailWireRangeAsync(long mailId, CancellationToken token);
}

public interface IGmMailRuleService
{
    GmMailInput Normalize(GmMailInput input, bool sending);
    string DisplayText(GmMailInput input);
    string PayloadHash(GmMailInput input);
    ValueTask<IReadOnlyList<GmMailPreparedItem>> PrepareItemsAsync(GmMailInput input, DateTimeOffset now, CancellationToken token);
}

public interface IGmOfficialMailDomainService
{
    ValueTask<GmMailQueryResult> QueryAsync(string secret, GmMailQuery kind, GmPlayerFilter filter, Guid id, GmMailInput? input, CancellationToken token);
    ValueTask<GmMailDocument> EditAsync(string secret, GmMailEdit kind, Guid id, GmMailEditorRequest input, CancellationToken token);
    ValueTask<GmMailSendResult> SendAsync(string secret, GmMailPreparation preparation, CancellationToken token);
    ValueTask<bool> RecordFailureAsync(string secret, Guid requestId, string outcome, string reason, CancellationToken token);
}
