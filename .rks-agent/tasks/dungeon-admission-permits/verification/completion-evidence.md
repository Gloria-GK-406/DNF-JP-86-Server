---
kind: rks-verification
decision: pass
---

# 入场资格凭证重构：完成验证

## Claim / Target

2026-10-11；基线 67ffa14f82e8e789e53e7a22fe1af1b85627d57e；分支 task/dungeon-admission-permits。当前完整实现满足 TaskContract revision 1 和 spec 的自动验收条件。未部署，未进行本轮 A21 实机验收。

独立审查为 [approved](../reviews/change-review.md)。[审查目标指纹](review-target.json) 与最终受测源码一致；未在审查通过后改变产品/测试代码。

## Required Checks

实际 PVF：`E:/Project/DNF/DNF-JP-86-Server/artifacts/avatar-socket-text-20261011/Script.server.pvf`，以 `PVF_ARCHIVE_PATH` 提供。

1. `dotnet build Server/86JP.Server.slnx -c Release -m:1`：exit 0，0警告/0错误。
2. `dotnet test Server/86JP.Server.slnx -c Release --no-build --no-restore -m:1 --logger trx --results-directory artifacts/admission-verification/final/test-results --blame-hang-timeout 3m --blame-hang-dump-type none`：exit 0，8项目共6786项；6784通过、0失败、2跳过。
3. `git diff --check`：exit 0；工作区HEAD和源分支HEAD仍为预期基线，审查指纹完全一致。

[逐项目结果、命令、TRX路径与SHA-256](test-results.json)；原始构建/测试日志：`artifacts/admission-verification/final/build.log`、`test.log`。原始日志位于保留工作区，汇总与哈希随本地结果提交保留。

两条跳过项是 PvfTownDefinitionSourceTests / PvfQuestDefinitionSourceTests 的 A_missing_archive_*：源码明确在能找到PVF时跳过“完全无归档”场景。本轮实际内容、应用层3625项、协议1439项、宿主159项均无跳过。

## Conditions

| 条件 | 结果 | 决定性证据 |
|---|---|---|
| SC-1 三阶段与目的地分离 | pass | 独立完整调用链审查；普通/循环→Visit→Admission，主动深渊/镜像/异界→Admission→EnterGranted；布局构建仅在消费后回调 |
| SC-2 免费凭证与疲劳 | pass | Free_party_permits_*；既有 Visit 零疲劳、出生房/新房间/重访、耗疲劳豁免判据全部通过；Costs中不存在疲劳类型 |
| SC-3 原材料/替代票/次数语义 | pass | DungeonEntryMaterialTests、HellDungeonEntryTests、MirrorDungeonEntryTests、DimensionDungeonTests；同ID合并、不同ID普通材料拒绝、账号材料、持有不扣、组队槽位、跨日次数 |
| SC-4 失败整批回滚 | pass | 真实SQL第二成员扣费失败、quota第二成员写失败、收费后Cell创建失败、正常返回拒绝后调用Commit、镜像布局中会话换代；DomainTransaction/UnitOfWork rollback-only判据 |
| SC-5 一次性及复核 | pass | Foreign_and_cross_transaction_permits_*、Free_party_permits_*、Changed_material_balance_*、Payment_recheck_*；收据一次性与完整参与者/地下城绑定由独立代码审查确认 |
| SC-6 协议兼容及接线 | pass | Protocol完整1439项通过，Registered_CMD_manual_entry通过，CycleQuestFlow与当前PVF入口判据通过；修复并复核普通run再挑战替换行为 |

## Evidence Boundary

本次证明代码路径、实际PVF规则、隔离SQLite事务和自动协议判据；不代表新增玩法已实现或本轮客户端已验收。未修改客户端、PVF、真实角色数据或现有部署。下一次部署后可实机复核普通/循环再挑战、主动深渊缺票/成功、镜像/异界次数与失败退款。
