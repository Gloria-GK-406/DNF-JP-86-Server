"""Read-only production source scan; refresh the committed Markdown inventory, fail on unknown files.

This is a text audit, not a compiler call graph. Semantic review is still required.
Run from any cwd: python path/to/scan-reward-routes.py
"""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[4]
groups = {
    "统一奖励/共享Effect": "ItemDeliveryDomainService ItemDeliveryRuleService InventoryEffectHandlers QuestCompletionEffectHandlers OpeningEffectHandlers ItemGrantWireProjection SelectCardCommand GrantTutorialRewardCommand AcceptQuestCommand PendingQuestItemGrants ApplyMonsterKillCommand ApplyObjectDestructionCommand DailyRefillDomainService DespairTowerDomainService IllusionTowerRewardDomainService JoustDomainService WeeklyAttendanceDomainService QuestHelperPuppetDomainService",
    "奖励计划/投影，不直接制造": "ApplicationModule QuestCompletionEffectRuleService MirrorAradQuestRuleService EquipmentEffectPlanRuleService InventoryEffectPreflightDomainService EffectOperationContext OpeningInventoryDomainService FinishQuestCommand",
    "共享制作/礼包计划，制作显式InventoryOnly": "OpeningEffectPlanRuleService RecipeCraftDomainService",
    "底层容器/生命周期": "ItemGrantDomainService ItemGrantDelivery ItemLifecycleDomainService ItemContainers CharacterBusinessInventoryDomainService",
    "购买/制作": "CeraShopDomainService NpcShopDomainService AvatarCompoundDomainService",
    "初始资产": "CharacterRosterService InitializationAssetGrants",
    "地面拾取/临时塔物品": "GroundPickupSlice TowerInventoryDomainService IllusionTowerDomainService",
    "专属称号簿解锁与镜像": "TitleBookDomainService",
    "显式GM邮件/邮件发送领取": "GmOfficialMailDomainService MailItemTransfers MailboxDomainService MailboxSendDomainService",
    "移动/排序/历史位置修正": "ItemMovementDomainService InventorySortDomainService LegacyWornItemNormalizer MoveItemSpaceSlice",
    "非物理资产/技能持久化": "GmCeraCommands PlayingCharacter PlayingCharacter.Experience EpicBookDomainService CharacterSkillStateDomainService",
    "既有通知投影，不制造物品": "CardRewardHandlers DungeonCardPresentationCoordinator CharacterSelectionDelivery DungeonClearSlice IllusionTowerPartyCoordination MonsterDeathSlice OnlineUserConnectionObserver PartyCommandSlice SelectCharacterHandler SupplementalSynchronizationMessages",
    "非物品创建/基础分发": "AuthenticationDomainService CharacterDomainService DungeonCellFactory DungeonVisitDomainService CommandBus DomainEventDispatcher EventService NetworkMessageDispatcher NotificationBus",
}
known = {name: group for group, names in groups.items() for name in names.split()}
pattern = re.compile(r"IItem(?:Grant|Delivery)DomainService|Grant(?:PreparedItem|BasicCharacterItem|Stackable)Effect|\.\s*(?:CreateAsync|PutNewAsync|PutAsync|AddAsync|GrantAllAsync|GrantAllPersistentAsync|GrantAsync|ApplyPlannedAsync|SendPreparedAsync|DeliverAsync)\s*\(")
rows, unknown = [], []
files = []
for project in ("Dfo.Server.Application", "Dfo.Server.Protocol"):
    files.extend(file for file in (root / "Server/src" / project).rglob("*.cs")
                 if not any(part in ("bin", "obj") for part in file.parts))
for file in sorted(files):
    if any(part in ("bin", "obj") for part in file.parts):
        continue
    hits = [(n, pattern.findall(line)) for n, line in enumerate(file.read_text(encoding="utf-8-sig").splitlines(), 1) if pattern.search(line)]
    if not hits:
        continue
    group = known.get(file.stem, "未归类")
    path = file.relative_to(root).as_posix()
    if group == "未归类":
        unknown.append(path)
    rows.append((path, group, ", ".join(str(n) for n, _ in hits)))
output = ["# 奖励路径反向扫描", "", "扫描当前生产源码，分类依据见 [迁移矩阵](reward-migration.md)。文本命中含非物品创建和投影；不能将命中数当奖励点数。", "",
          f"命中文件 {len(rows)}；未归类文件 {len(unknown)}。", "", "| 文件 | 裁定 | 命中行 |", "|---|---|---|"]
output.extend(f"| [{path}]({(Path('../../../../') / path).as_posix()}) | {group} | {lines} |" for path, group, lines in rows)
(root / "Document/tasks/item-delivery/migration-scan.md").write_text("\n".join(output) + "\n", encoding="utf-8", newline="\n")
print(f"Scanned {len(files)} source files; {len(rows)} matching files; {len(unknown)} unclassified.")
for path in unknown:
    print("UNCLASSIFIED", path)
    for n, line in enumerate((root / path).read_text(encoding="utf-8-sig").splitlines(), 1):
        if pattern.search(line): print(f"  {n}: {line.strip()}")
raise SystemExit(1 if unknown else 0)
