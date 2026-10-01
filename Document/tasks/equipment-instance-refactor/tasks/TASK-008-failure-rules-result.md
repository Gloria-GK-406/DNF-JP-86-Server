# TASK-008 实现自检结果

状态：实现交付并冻结 Server 目标；完整自检的最终结果见下方。独立完整复核、Release/真实 DI 副本探针、部署和实机最终验收由 root 继续负责，本文件不封存根契约。

## 实际结果

- `IEquipmentReinforcementRule.TryReinforce(draw)` 仅返回成败；处罚、保护消费和完整产物由 `IEquipmentReinforcementFailureRuleService.GetResultAsync` 返回。
- `GetResultAsync(instance, context, ct)` 是薄只读扩展：先 `ReadAsync` 冻结 `EquipmentInstanceFacts`，再交给只依赖静态 reinforcement/disjoint source 的实现；没有查询角色/背包或修改实例。
- 失败结果包含保持、降级、销毁、目标等级、券消费和全部奖励列表。处罚 override 有唯一纯 helper，报价只取得抽前选券所需的 `CanDestroy` 提示。
- 损毁基础产物采用 70 `DisJoint::GetResult` 路径，保留原 value 的 float32 1.1 修正、卖价率、整数分解基数和品质系数。随机属性模板读取第三卖价率，普通模板读取第一卖价率；基础追加用 grade、float32 商临时量和截断，不调用 NPC admission/fraction/expand。
- 追加强化材料按原等级计算。当前静态白装 12866 / value 20520 / grade 57 / rarity 0 / rate 200 得到 33 无色，没有强化追加痕迹；稀有 +11 冲 +12 损毁追加 1 痕迹，原来的目标等级公式 4 已关闭。
- 领域预先模拟所有物理产物之间的槽位竞争、叠堆、付款释放槽和账号材料扣费后容量；任何奖励放不下、未知内容或溢出均拒绝，实际扣费、变更、发奖统一事务。
- 虚拟材料也进入现有 ACK 的短槽位奖励结构；提交后 NOTI 对同虚拟槽取最终余额，ACK MaterialRemaining 使用本次全部结算后的余额；协议编码布局不变。

## 回归证据

共同工件根：`E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-reinforcement-failure-20260930/`。

| 工件 | 结果与用途 |
|---|---|
| `tests/failure-red.trx` | TDD 预期两个真实 SQLite 失败：追加材料应 1 实际 4；白装损毁无色实际 0 |
| `tests/failure-green-extended.trx` | 中间专项 108 通过；覆盖白/稀有、保护不产物、多奖励争槽、虚拟容量、后续发奖失败整体回滚、只读 instance adapter、ACK/NOTI/replay/重登 |
| `tests/application-full.trx` | 中间无 PVF 全量 2198 通过、171 条因缺档案跳过，不能代替真实内容覆盖 |
| `tests/application-full-real-pvf.trx` | 中间真实 PVF 全量 2381 通过、0 失败、0 跳过；在最后整数 chance / 溢出 guard / 单一 penalty helper 完成前编译，仅作阶段证据 |
| `tests/infrastructure-full-real-pvf.trx` | 最终生产源码，真实 PVF 412 通过、0 失败、2 条 missing-archive 条件跳过；新增原 value/grade、普通/随机卖价率及 NPC 行为不变验证 |
| `tests/application-final-real-pvf.trx` | 最终生产源码及最后 float32 / Int32 边界 / integer chance 用例，2383 通过、0 失败、0 跳过（2分19秒）；包含完整专项 |
| `worker-source-manifest.json` | 冻结的 17 个 worker 目标路径/SHA-256；仅这份目标列表由本 worker 实施，所有其他原未提交结果保留 |

`git diff --check -- Server` 已通过。旧 `EquipmentReinforcementAttempt`、`EquipmentReinforcementRules.Decide`、`DestroyBonusCount` 和 `TryDestroyBonus` 入口已无引用。

## 实机与边界

未操作生产服务、生产 DB、客户端或 PVF，未重发资金/材料或恢复被销毁装备。尚未将任何新行为标记 ClientVerified。根契约继续要求 [F 实机列表](../Failure-Human-Test-List.md) 的当前版本用户反馈和正常重登一致性。
## 独立复核返修：实际长剑技能嵌套字段

实际 DI 副本探针发现 UID689 对应长剑 101040127 有根级 `[value]=104960` 和三条技能条件内 `[value]=1`。原先平铺 `Sections` 后要求全文件唯一 value，导致有效装备返回 UnsupportedOutcome；第一轮白装/基础模板与全量测试不能证明这件真实长剑的失败内容可用。原失败 probe 作为发现证据保留，不将其写成成功。

返修只改变新 `ReadDestructionEquipment` 的字段读取：显式 `[/tag]` 对应的容器建立作用域栈，筛出根级属性；已确认的 skill 容器也要求闭合，重复根字段仍拒绝，闭合错配和未闭合拒绝。value、grade、rarity、random option 全部经过同一根级过滤。NPC 的原平铺解析没有修改。

- `tests/nested-value-red.trx`：实际长剑和合成三层技能结构，预期 2 个失败。
- `tests/nested-value-green.trx`：14 通过、0 失败、0 跳过；包括实际长剑、嵌套的 value/grade/rarity/random option、重复根字段、错配/未闭合及缺少根 value。
- `tests/infrastructure-root-scope-final-real-pvf.trx`：最终真实 PVF 全量 421 通过、0 失败、2 条 missing-archive 条件跳过（1分25秒）。
- `tests/application-root-scope-associated.trx`：关联强化/分解领域回归 134 通过、0 失败、0 跳过；Application 业务源码未改，之前完整 2383 通过作为未变业务边界证据保留，不冒充新 Infrastructure 的全量身份。
- `worker-source-manifest.json` 已更新为 revision 2；旧身份保存在 `.rev1`，目标仍为 17 个。本次返修仅其中 Infrastructure source 与其 tests 两个路径变化。

后续仍由 root 以新的真实 UID689 GetResult 非拒绝断言、独立复核和部署检查确认结果；本 worker 未操作生产资产。