using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Characters;

public sealed record CharacterSkillLearningChange(ushort SkillId, byte Refund, byte Increment);

public enum CharacterSkillLearningRejection
{
    None, NoSession, InvalidRequest, UnsupportedScenario, PageUnavailable,
    SkillUnavailable, InsufficientPoints, NoSlot, IncompleteLedger, ResetItemRequired,
}

public sealed record CharacterSkillLearningReceipt(
    SelectedSessionIdentity Session, int PageIndex,
    IReadOnlyList<CharacterSkillState> Skills, CharacterSkillPointLedger Ledger, Dfo.Server.Data.Inventory.OpeningInventoryReceipt? Inventory = null);

public sealed record CharacterSkillLearningResult(
    CharacterSkillLearningRejection Rejection, CharacterSkillLearningReceipt? Receipt = null);
