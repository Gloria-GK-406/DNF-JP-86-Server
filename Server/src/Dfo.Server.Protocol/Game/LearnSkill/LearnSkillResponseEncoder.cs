namespace Dfo.Server.Protocol.Game.LearnSkill;

internal sealed class LearnSkillResponseEncoder : GameCmdMessageEncoder<LearnSkillResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(LearnSkillResponse message) =>
        OnePayload(message, EncodeBody(message));

    internal static byte[] EncodeBody(LearnSkillResponse message)
    {
        // A21 accepts 00 03 and runs the failure cleanup. The meaning of 03 is a
        // project compatibility choice; no precise client error text is claimed.
        if (message.Receipt is not { } receipt) return [0, 3];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var points = receipt.Ledger.ToProtocolMirror();
        writer.Write((byte)1);
        writer.Write(checked((byte)receipt.PageIndex));
        writer.Write(receipt.PageIndex == 0 ? points.Page0Sp : points.Page1Sp);
        writer.Write(receipt.PageIndex == 0 ? points.Page0Tp : points.Page1Tp);
        writer.Write(checked((byte)receipt.Skills.Count));
        foreach (var skill in receipt.Skills)
        {
            writer.Write(checked((byte)skill.Slot));
            writer.Write(checked((ushort)skill.SkillId));
            writer.Write(skill.Level);
            writer.Write((byte)(skill.ExtraValues.Count == 0 ? 0 : 1));
            if (skill.ExtraValues.Count == 0) continue;
            writer.Write(checked((byte)skill.ExtraValues.Count));
            foreach (var value in skill.ExtraValues) writer.Write(value);
        }
        return stream.ToArray();
    }
}
