---
kind: rks-review
verdict: approved
---

# Change review：账号级晶体与品级灵魂背包（第二轮）

## 审查目标

- 基线：`b94308ce42585ac0b88e303b0010131931b74265`
- 目标：`task/account-material-bags` 当前未提交工作树（包括全部未跟踪文件）
- 范围：TaskContract revision 1、`spec.md`、`plan.md` 所约束的完整代码与文档混合交付
- 标准：`reviewing-work` 的 complete code + deliverable review
- 复审重点：首轮 F-001/F-002、账号材料单一真源、同账号共享/跨账号隔离、SQLite 原子事务、
  NPC/便携分解回滚、宇宙灵魂例外、`0x000D/0x000E` 投影、文档证据边界、无自动数据迁移与旧旁路删除

## Verdict

`approved`。首轮两项阻塞发现均已闭合，当前完整 changed target 满足 TaskContract/Spec 的验收边界。
没有发现需要继续修改的 code 或 deliverable 问题。

## 首轮 findings 处置

### F-001：resolved

- 新增必需依赖 `IItemGrantDomainService`，在领域边界按 itemId 将到货分流为账号材料或角色物品。
- 地面拾取、翻牌、接任务、交任务、怪物任务直发和教程奖励等全部现有生产 `AddAsync` 到货入口
  已改为调用该服务；全仓生产代码搜索后，角色容器 `AddAsync` 的直接调用仅保留在该统一服务内部。
- `StoreBackedItemContainer.WriteOneAsync` 在所有 `PutAsync`/`PutNewAsync` 最终公共写边界硬拒绝十一种
  账号材料，因此 NPC 商店、开盒、CERA 商店或未来 direct-slot planner 即使尚未接入账号材料语义，
  也只能使所在事务失败并回滚，不能静默创建角色级第二真源。
- 非分解教程奖励回归测试证明账号余额增加且角色 `item_slots` 不落行；失败注入证明账号余额与角色
  到货一起回滚；direct Put 回归测试证明旁路被拒绝。
- `GrantTutorialRewardCommand.AccountId` 已改为必填，各生产 handler 的 Grant 服务依赖也均为必需项，
  没有默认账号或生产 fallback。

该硬拒绝策略符合本任务的单真源约束。它有意不替未建模的购买/开盒来源猜测账号材料发放语义；
这些路径若意外产出账号材料会显式失败，而不是写错所有权。该限制已在架构文档中如实说明。

### F-002：resolved

- 十一种资源的 kind、itemId 与虚拟槽映射已集中到 Data 层 `AccountMaterialCatalog`。
- `AccountMaterialRuleService`、入场 `0x000D` 固定虚拟条目和运行时账号材料逻辑都从该目录派生；
  协议编码器中的第二组硬编码数组已删除。
- 映射完整性/唯一性测试覆盖 `354..364`，消除了登录全量快照与运行时增量各自漂移的生产定义。

## 完整交付核验

- **所有权与隔离：** `AccountMaterialBagDomainService` 按 `AccountId` 缓存逻辑实例，持久化以
  `accounts.account_id` 为键；测试覆盖同账号跨角色相同投影、同账号实例复用及不同账号余额隔离。
- **事务：** `SqliteAccountMaterialBagStore` 与 `SqliteItemDomainStore` 对同一数据库路径生成相同
  `StoreIdentity`，因此共享一个 `SqliteDatabaseParticipant`。NPC 分解、便携分解、普通材料混合结算及
  账号写入失败测试证明装备删除、工具消耗、角色材料和账号余额共同提交或共同回滚。
- **分类：** 六种晶体与五种品级灵魂只写账号行；宇宙灵魂 `3257..3260` 不在目录中，仍按角色
  普通材料容量、堆叠和槽位规则结算。
- **协议：** 入场 `0x000D` 从同一目录生成 `354..364` 固定虚拟槽；分解 ACK 保留本次产量，
  `0x000E` 发送事务后的绝对总量。非分解发放路径也对当前操作角色发送相同绝对值更新。
- **持久化：** 六种晶体沿用账号列，新增五种品级灵魂账号列；没有扫描、迁移、合并或删除历史
  `item_slots` 中错误数据的代码，符合用户手工清理与“不自动迁移”的约束。
- **旁路：** 旧 `FindCubeFragmentsAsync` / `CubeFragmentRow` 已删除；生产代码中的十一项 itemId
  只在 `AccountMaterialCatalog` 定义一次。容器最终写边界阻止 direct-slot 形成旧式角色记录。
- **文档：** `Document/AccountMaterialBag.md` 与 `NOTI-0x000D/0x000E` 明确区分 A21 线路/PVF 证据、
  本服账号级玩法策略和仍待实机验证的客户端效果，没有把本服持久化策略误标成 `ClientVerified`。

## 检查证据

- `git diff --check b94308ce42585ac0b88e303b0010131931b74265`：通过。
- `dotnet build Server/86JP.Server.slnx --no-restore`：通过，0 warning / 0 error。
- 设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 后独立运行：
  - `Dfo.Server.Application.Tests`：1634/1634 通过；
  - `Dfo.Server.Persistence.Tests`：355/355 通过；
  - `Dfo.Server.Protocol.Tests`：977/977 通过。

## 非阻塞边界

- 旧错误角色材料行不会被本任务自动处理；用户需按契约在实机测试前手工清理。
- 尚未建立账号材料业务语义的 direct-slot planner 目前会事务失败；后续若明确支持该来源，应复用
  `IItemGrantDomainService`，不能放宽容器写入守卫。
- `0x000E` 的分解后客户端刷新效果仍需 A21 实机验收；当前自动化已闭合编码形状、消息顺序和
  绝对值语义，但没有将未执行的实机观察提升为协议事实。
