# 地下城入场限制与拒绝原因

2026-10-07；用户授权：切分不同入场限制问题，实现后部署。工作区 `task/abyss-research`，基线 `33c01f3f`。本次仅修改服务端，没有客户端 patch 或 PVF 修改。

## 实现

`Package Handler → EnterDungeonCommand → 普通/主动深渊领域服务 → 地下城规则服务 → PVF`。拒绝保留具体领域原因和成员槽位，协议层只做明确映射。拒绝恢复选图意图，可再次请求；不创建 run、不扣材料或疲劳。

| 领域原因 | A21 CMD0010 错误码 | 客户端用途 |
| --- | --- | --- |
| DungeonNotFound / DifficultyNotUnlocked | 15 | 地下城或难度不符 |
| MissingRequiredItem | 11 | 普通材料或费用不足 |
| MissingHellTicket | 1B | 深渊门票不足 |
| LevelTooLow | AC；主动深渊 6F | 等级不足 |
| MissingPrerequisite / HellQuestNotCleared | 07 | 前置条件不满足 |
| InsufficientFatigue | 16 | 疲劳不足，携带实际成员槽位 |
| AlreadyInDungeon / 选图成员会话不可用 | 13 | 当前选择状态不可用 |
| MemberEntryLimitReached | AD | 成员次数/权限限制 |
| DailyEntryLimitReached | F6 | 每日次数上限 |

次数原因仅建立独立语义与映射，**本次没有新增每日限额、次数表或计数规则**。旧 `MemberBlocked` 泛化名已清除；塔的材料不足、等级不足和会话失败也分别映射，不再共用 AD。

`DungeonDefinition.RequiredItems` 来自 `[required item]` 的物品/数量/消耗标志三元组。规则结构仍通过 `IDungeonStructureRuleService` 获取；新增 `IDungeonEntryMaterialsDomainService` 先检查整队并生成一次性消耗计划，成功入场后在同一事务扣除。查角色携带空间及账号 SharedMaterial，不查仓库；跨堆叠合计、仅持有不消耗、共享虚拟槽余额投影均支持。房间创建、扣除或提交异常仍使用现有事务回滚。

PVF 声明的最低难度直接可用，其他声明难度仍需通关解锁；不允许请求未声明的难度。固定远古难度不再被最大解锁值 0 误拒绝。最低等级检查按成员执行；带 `[story mode]` 的剧情图保留由任务控制的等级豁免。深渊既有任务要求继续检查。

机械牛 1500 的当前 PVF：最低等级 85、固定难度 2、无色小晶块 3037 ×120、灵魂晶石 3332 ×10，均在成功入场时消耗。深渊额外成本与普通材料重合时，先校验总需求，再按普通扣除后的当前堆叠重新分配深渊扣除，保留两部分余额投影。

## 证据与验证

- CurrentImplementation：上述领域服务、PVF 内容投影、协议映射及测试。
- ClientStatic：`artifacts/manual-hell-refusal-20261007/localized-error-codes.json` 和原生拒绝跳表，标识该 A21 客户端实际文案；11、1B、AD、F6 并非同一拒绝原因。
- PVF：实际运行档案 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 的 `dungeon/Ancient/N_Vilmark.dgn`。
- 自动验证：全解决方案编译通过；Application 124、Protocol 115、Infrastructure 22 项通过，PVF 测试未跳过。覆盖整队拒绝、事务提交/回滚、创建房间异常、共享无色、跨堆叠扣除、同物品普通/深渊费用和固定难度。

## 实机验收与边界

待用户验证：等级满足时普通远古缺材料显示材料不足；材料齐全时成功入场并只扣一次；固定难度可进入；拒绝后可重试和返回城镇。新回包的完整客户端体验尚未 ClientVerified。

已知独立问题：主动深渊缺票后地图/难度缓存锁定没有在本轮解决，见 `abyss-research-20261007/manual-hell-refusal-ui-lock.md`。随机深渊仍为原有桩。世界地图免费通行券替代普通成本的规则尚未接入本次 `[required item]` 检查，不宣称完整实现所有特殊准入政策。

部署身份、原运行目录、文件哈希、数据库备份及健康检查保存到仓库根目录 `artifacts/dungeon-admission-20261007-<commit>/`。
