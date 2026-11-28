---
kind: rks-verification
decision: pass
---

# 服务端完成验证

验证产品提交 `baa1fe099`。解决方案构建通过，零警告、零错误。独立复审覆盖当前产品提交。[改动评审](../reviews/change-review.md)

| 契约条件 | 核对结果 |
|---|---|
| SC-1 | `GameplayBenefitRegistryTests` 验证作用范围、来源归属、顺序、时间窗口、较新整批替换、空声明撤回和读取后快照独立。写入边界和状态所有者普查通过。 |
| SC-2 | `ExperienceBonus`、`ExperienceCalculation`、`LuckyCharacter` 回归验证经验来源、正常副本及适用塔入口、任务交付、队友经验和疲劳。结算恢复沿用原存档金额。 |
| SC-3 | `DropRateEvent`、`GameplayParameterFreeze`、`MonsterDropDomainService`、`DungeonCellFactory`、任务材料及 Heras 专项通过，覆盖全局倍率、房间冻结、单队份额、神器限定和共享归属。 |
| SC-4 | 使用当前 dev PVF 的强化药回归验证消耗回滚、持久截止时间、同角色重登、另一角色隔离、到期、一次强化抽样及旧数值保留。`EquipmentReinforcement` 和 `EquipmentAmplification` 验证券、保护、增幅及材料路径。v49 → v50 迁移测试确认旧行完整和新表初始为空。 |
| SC-5 | 对照代码中的生产来源和消费入口复扫，记录三类适用加成及仍独立的规则。[复扫结果](benefit-audit.md) |

- 使用当前 dev PVF 的应用定向 376 项，协议定向 209 项，持久化定向 59 项，数据库迁移全套 74 项全部通过。命令使用 `dotnet test --no-build`，PVF 用例设 `PVF_ARCHIVE_PATH` 指向开发通道。
- 应用全量在无 PVF 环境完成 7310 项，其中 6642 通过、659 跳过、9 失败。按用例名与 `f4a413bcf` 的同环境基线比较，9 项均可在基线复现。基线另有一项幸运疲劳写边界分类失败，在本分支通过。
- 新旧数据库形状和行为只在隔离测试库验证。A21 客户端的强化药显示仍待实机核对。

## 主干同步后的 PR 验证

产品提交 `efdecc5ae` 基于 `2d4d5b179`。Release 构建零警告零错误，协议定向 416 项、数据库迁移 74 项通过。应用定向 665 项通过，幻影塔任务材料回滚用例仍因重试后材料数为 0 失败，已用同一份开发 PVF 在重构前基线复现。用户实机确认的范围见[幸运角色实机结果](../../lucky-character-event/user-test.md#本轮实机结果)。
