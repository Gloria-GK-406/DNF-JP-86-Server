---
kind: rks-review
verdict: approved
---

# Change Review：任务转职奖励与技能状态同步

## 评审目标

- 基线：`6672b21f19435e69d97a6af190b37a938fa91a41`
- 目标：`feat/quest-career-reconciliation` 当前完整工作树，包含未跟踪文件
- 契约与规格：`../TaskContract.md`、`../spec.md`
- 实现说明：`../implementation.md`
- 评审准则：`reviewing-work` 的代码评审准则，以及仓库 `AGENTS.md` 的架构、协议证据和技能兼容规则

## 独立结论

**approved**。当前实现满足 SC-1 至 SC-4 / AC-001 至 AC-004，未发现剩余阻塞缺陷。

本结论批准的是当前代码结果及其自动验证证据。特殊转职 ACK 和通知顺序仍是 86JP 参考代码支持的候选行为，尚未提升为 A21 `ClientVerified`；这与规格明确保留的用户实机验收边界一致，不构成本轮代码评审阻塞。

## 关键证据

### 1. PVF 奖励与原子结算

- `QuestRewardRuleService` 解析单一合法 `GrowType` 奖励，并拒绝重复、缺值及 1..15 之外的定义。
- `FinishQuestCommandHandler` 明确要求事务执行，在任务、物品/金币、经验之后调用职业领域服务；职业或技能写入失败会转成结算拒绝，由事务边界整体回滚。
- `CharacterCareerDomainService` 校验所有者、当前未转职状态、目标职业规则和属性规则；`SetGrowTypeAsync` 使用旧值条件更新，避免并发覆盖。
- 真实 PVF 7840 测试同时覆盖空技能树成功路径、注入技能写入失败后的任务/经验/职业/技能全回滚，以及重复完成不重复奖励。

### 2. 技能整理

- `CharacterSkillStateDomainService.ReconcileAsync` 在同一领域事务中处理 PVE 两页：仅删除规则已知且对目标职业确认为不适用的技能；保留适用技能的等级、槽位和扩展配置；按现有规则补足免费/固定等级并分配空槽。
- 已有高等级不会在转职整理中被降级，符合达人契约到期由其他所有者处理的边界。
- 规则未建模的已学技能保留原等级、槽位和扩展值，并继续让账本标记为 incomplete，符合仓库既有兼容语义。
- 测试覆盖两页、付费等级、快捷槽、免费技能、点数账本、幂等、合法越级、未知技能保留与写入失败回滚。

### 3. 消息顺序、属性与身份刷新

- 成功转职后重新读取提交后的 current view，并单独发送完整 `SkillStateMessage`；随后发送 0x0022 ACK，再发送新的角色状态与任务投影，满足 `0013 → 0022 → 新状态`。
- 自身同时收到 `CurrentUserStateMessage` 和携带新基础属性的 `ExtendedUserStateMessage`；装备、背包、经验、点数与身份均来自提交后的投影。
- 状态批次之后复用 `WeaknessProjectionDelivery.SendCurrentAsync` 重投影权威虚弱值，避免通用状态中的持久化耐力值覆盖当前虚弱状态；测试以 28 的非默认值验证最终消息，并让所有虚弱变更方法抛错以证明此路径只读。

### 4. 公共投影范围与会话代次

- 广播前重新确认源角色仍属于同一 selected session。
- 镇区接收者来自 `VisibleTo`，并逐个要求当前 session 持有对应 `TownAreaMember` lease；私有房间不外发。
- 地下城源和目标都必须属于 `DungeonRunDeliveryRegistry` 冻结的 admitted generation，目标还必须仍在同一 run；重连后的新 session 不继承旧 run 流量。
- 七组协议测试覆盖正常镇区/地下城广播、替换目标、私有房间，以及第二次 current-view 读取后替换源 session 的镇区和地下城竞态。

### 5. ACK 证据边界

- 普通完成 ACK 编码分支保持既有抓包固定形状。
- 特殊转职 ACK 的 17 字节 chain-1 分支与 `E:/Project/86JP` 的 `QuestAckBuilder.BuildFinish` 对照一致；协议记录明确标成上游代码推导、尚未 `ClientVerified`，没有用单元测试或参考服行为冒充真实 A21 客户端效果。
- `Document/转职任务验收.md` 给出用户后续实机步骤；本次没有部署服务或改动玩家数据。

## 评审中发现并已修复的问题

1. 初版仅发送 subtype 0，不能刷新自身基础属性，并可能让当前虚弱投影回到持久化默认值。现已补 subtype 1 和权威虚弱重投影。
2. 初版遇到技能规则缺失时中止整理。现已保留未知已学行及其槽位/配置，并保持 incomplete ledger。
3. 初版公共广播把成员降成 `CharacterId`，可能把旧镇区 membership 或旧 dungeon run 的投影发送给替换连接。现已对源与目标执行发布时的精确代次校验。

三项均已通过代码复核和针对性回归，不再是活动 finding。

## 验证记录

- `CareerReconciliationTests`：8/8，通过，包含两例真实 PVF 7840。
- `QuestCompletion` 定向集：34/34，通过。
- Application 全量：1703/1703，通过。
- Persistence 全量：361/361，通过，设置了 `PVF_ARCHIVE_PATH`。
- Protocol 全量：1026/1026，通过。
- Host 全量：17/17，通过。
- Solution build：0 错误、0 警告。
- 本评审执行 `git diff --check`：通过。

上述测试结果由执行控制器提供；本评审按只读约束复核代码、差异、测试设计和结果，没有重复运行重型全量套件。

## 非阻塞剩余验证

- 由用户按验收文档在真实 A21 客户端完成任务 7840，确认特殊 ACK 被接受、技能页即时刷新、属性/外观/同行可见身份正确。
- 实机通过后再更新协议证据等级；在此之前继续保持特殊 ACK 与顺序为上游代码推导证据。
