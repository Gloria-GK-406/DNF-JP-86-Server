using System.Text.Json;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Marriage;

internal sealed class SqliteMarriageProposalDomainStore(SqliteDatabase database) : IMarriageProposalDomainStore
{
    public ValueTask<MarriageProposal?> FindActiveAsync(CharacterId character, CancellationToken ct) =>
        FindAsync(character, true, ct);
    public ValueTask<MarriageProposal?> FindLatestReceivedAsync(CharacterId character, CancellationToken ct) =>
        FindAsync(character, false, ct);

    private async ValueTask<MarriageProposal?> FindAsync(CharacterId character, bool active, CancellationToken ct) =>
        (await database.ReadAllAsync("""
            SELECT r.payload_json,r.state,r.revision,r.response
            FROM marriage_proposals r JOIN marriage_proposal_participants p ON p.proposal_id=r.id
            WHERE p.character_id=@character
            """ + (active ? " AND p.active=1" : " AND p.role=1") + " ORDER BY r.created_at DESC,r.rowid DESC LIMIT 1",
            p => p.AddWithValue("@character", character.Value), r =>
                (JsonSerializer.Deserialize<MarriageProposal>(r.GetString(0))
                    ?? throw new InvalidDataException("Invalid marriage proposal.")) with
                { State = (MarriageProposalState)r.GetInt32(1), Revision = r.GetInt64(2),
                    Response = r.IsDBNull(3) ? null : checked((byte)r.GetInt32(3)) }, ct)
            .ConfigureAwait(false)).SingleOrDefault();

    public async ValueTask CreateAsync(MarriageProposal proposal, CancellationToken ct)
    {
        await database.ExecuteAsync("""
            INSERT INTO marriage_proposals(id,payload_json,state,revision,created_at,response)
            VALUES(@id,@payload,0,1,@at,NULL);
            INSERT INTO marriage_proposal_participants(proposal_id,role,character_id,active)
            VALUES(@id,0,@first,1),(@id,1,@second,1);
            """, p => { p.AddWithValue("@id", proposal.Id.ToString("N"));
                p.AddWithValue("@payload", JsonSerializer.Serialize(proposal));
                p.AddWithValue("@at", proposal.CreatedAt.ToUnixTimeMilliseconds());
                p.AddWithValue("@first", proposal.Proposer.CharacterId.Value);
                p.AddWithValue("@second", proposal.Recipient.CharacterId.Value); }, ct).ConfigureAwait(false);
    }
    public async ValueTask<bool> TransitionAsync(MarriageProposal expected, MarriageProposalState state,
        byte? response, CancellationToken ct)
    {
        var updated = await database.ExecuteAsync("""
            UPDATE marriage_proposals SET state=@state,revision=revision+1,response=@response
            WHERE id=@id AND state=@old AND revision=@revision
            """, p => { p.AddWithValue("@id", expected.Id.ToString("N")); p.AddWithValue("@old", (int)expected.State);
                p.AddWithValue("@revision", expected.Revision); p.AddWithValue("@state", (int)state);
                p.AddWithValue("@response", response is null ? DBNull.Value : response.Value); }, ct).ConfigureAwait(false);
        if (updated != 1) return false;
        if (state is MarriageProposalState.Rejected or MarriageProposalState.Expired)
            await database.ExecuteAsync("UPDATE marriage_proposal_participants SET active=0 WHERE proposal_id=@id",
                p => p.AddWithValue("@id", expected.Id.ToString("N")), ct).ConfigureAwait(false);
        return true;
    }
}
