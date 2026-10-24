using Dfo.Server.Application.Execution.Outbox;

namespace Dfo.Server.Application.Commands;

internal sealed class CommandJobEntry(GameplayOutboxes outboxes)
{
    public void Begin() => _ = outboxes.Current;
}
