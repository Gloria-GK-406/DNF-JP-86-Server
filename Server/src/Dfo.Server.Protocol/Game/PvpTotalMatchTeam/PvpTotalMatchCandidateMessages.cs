using System.Collections.Immutable;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.PvpTotalMatchTeam;

internal sealed record PvpTotalMatchCharacterGroup(ushort WireUserId, ImmutableArray<PvpTotalMatchCandidate> Candidates);

internal sealed record PvpTotalMatchCharacterInfo(
    ImmutableArray<PvpTotalMatchCharacterGroup> Groups, DateTimeOffset PreparedAt) : IGameNotiMessage
{
    public PvpTotalMatchCharacterInfo(ushort ownerWireUserId, ImmutableArray<PvpTotalMatchCandidate> candidates,
        DateTimeOffset preparedAt) : this([new(ownerWireUserId, candidates)], preparedAt) { }

    public ServerMessageType MessageType => ServerMessageType.GamePvpTotalMatchCharacterInfo;
}

internal sealed class PvpTotalMatchCharacterInfoEncoder : GameNotiMessageEncoder<PvpTotalMatchCharacterInfo>
{
    private const int AppearanceTailLength = sizeof(uint) + 4;
    private const int CandidateTailLength = 7 * sizeof(uint);

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpTotalMatchCharacterInfo message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)message.Groups.Length));
        foreach (var group in message.Groups)
        {
            writer.Write(group.WireUserId);
            writer.Write(checked((byte)group.Candidates.Length));
            for (var index = 0; index < group.Candidates.Length; index++)
            {
                writer.Write(checked((byte)index));
                WriteCandidate(writer, group.Candidates[index], message.PreparedAt);
            }
        }
        return OnePayload(message, stream.ToArray());
    }

    private static void WriteCandidate(BinaryWriter writer, PvpTotalMatchCandidate candidate, DateTimeOffset preparedAt)
    {
        var identity = candidate.Character.Identity;
        var publicState = candidate.Public;
        var extras = publicState.Extras;
        writer.Write(identity.Name.Length);
        writer.Write(identity.Name);
        writer.Write(candidate.Character.Progression.Level);
        writer.Write(identity.Job);
        writer.Write(identity.GrowType);
        StateSynchronizationPayloadCodec.WriteExtendedCombatStats(writer, publicState.DerivedStats);
        writer.Write(checked((byte)extras.ExtraEquipmentSlotState));

        WriteAppearance(writer, publicState.Display?.Parts ?? []);
        // A21's candidate reader consumes these appearance values even when they are absent.
        writer.Write(new byte[AppearanceTailLength]);
        StateSynchronizationPayloadCodec.WriteNoti2Equipment(
            writer, candidate.Equipment, publicState.CloneTitleItemId, preparedAt);

        writer.Write((ushort)0);
        var learned = candidate.DuelSkills.Skills.Where(skill => skill.Level > 0).OrderBy(skill => skill.Slot).ToArray();
        writer.Write(checked((byte)learned.Length));
        writer.Write(checked((byte)candidate.DuelSkills.PageIndex));
        foreach (var skill in learned)
        {
            writer.Write(checked((byte)skill.Slot));
            writer.Write(checked((ushort)skill.SkillId));
            writer.Write(skill.Level);
        }
        writer.Write(checked((uint)extras.SpecialRewardQuestIds.Count));
        foreach (var questId in extras.SpecialRewardQuestIds)
            writer.Write(checked((uint)questId));

        // Unmapped candidate-state values. The waiting-room projection leaves them at zero.
        writer.Write(new byte[CandidateTailLength]);
    }

    private static void WriteAppearance(BinaryWriter writer, IReadOnlyList<CharacterRosterAppearanceState> parts)
    {
        const byte linkedAvatarSlot = 9;
        writer.Write(checked((byte)parts.Count));
        foreach (var part in parts)
        {
            // The appearance projection emits a link only for a worn avatar with a separate display template.
            var hasLinkedExpansion = part.Slot == linkedAvatarSlot && part.LinkItemId > 0;
            StateSynchronizationPayloadCodec.WriteAppearancePart(writer, part, hasLinkedExpansion);
        }
    }
}
