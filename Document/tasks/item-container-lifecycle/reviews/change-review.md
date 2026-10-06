---
kind: rks-review
verdict: approved
---

# 物品容器生命周期完整变更审查

## 审查目标与范围

- 目标分支：`task/warehouse-storage`。
- 基线与当前 `HEAD`：`32039260ded8d251c5052dfcec597213c561668a`；审查目标是该基线之上的完整冻结工作树差异，包含 tracked 与 untracked 的生产代码、测试和任务文档。
- 治理依据：`TaskContract.md` revision 1、`spec.md`、`plan.md`、仓库 `AGENTS.md`，以及 reviewing-work 的 Code Review 与 Deliverable Review 标准。
- 实际检查路径：`Server/src/Dfo.Server.Abstractions`、`Dfo.Server.Data`、`Dfo.Server.Application`、`Dfo.Server.Persistence`、`Dfo.Server.Protocol` 的全部本任务差异；`Server/tests` 下对应 Application、Persistence、Protocol、Infrastructure、Host、HttpApi、Networking、Pvf 测试差异；`Document/tasks/item-container-lifecycle` 与本任务更新的 `AGENTS.md`。迁移报告只用于定位，结论以源码、测试源码和最终验证工件为准。

## 结论

当前目标在上述范围内没有未解决的阻断缺陷。实现满足用户要求的生命周期区分，并保持既有事务、物品身份、状态和业务限制，因此批准进入控制器的完成验证与收口阶段。本批准是代码与交付物审查结论，不替代控制器按 Success Conditions 做最终完成判定。

## 决定性证据

### 生命周期与能力边界

- `ItemSnapshot` 成为业务侧完整属性值，覆盖 Core 字段、UID、时装和宠物细节；Core 编解码仍留在数据库和协议边界。
- `ItemRecord` 只由会话发行并按对象身份登记为 detached 能力；普通读取得到的是带容器、槽位和占用代次的 `ItemReference`，不能伪造为可接收记录。
- `Get` 不改变占用；`Take` 腾空来源并返回 record；`Put` 只接收仍有效的 record；`Update` 只修改当前引用代表的占用；`DeleteItem` 显式结束 detached 生命周期。Take、换位、同 UID 回原槽及事务结束都会使旧引用失效。
- 旧 `StoredItem`、`ContainerSlotItem`、`InventorySlotContent`、`InventoryItemCreationFacts`、`ItemTransferSource`、`PutTransferred`、`PutNew` 等生产业务通路已退出；剩余 `StoredItemCount` 只是账号仓库状态字段，并非被替代的物品模型。

### 事务、持久化与托管

- `ItemLifecycleScope` 先于存储 facet 加入同一 `DomainTransaction`，提交前拒绝未安置、未销毁的 record；事务结束关闭 scope，跨事务引用和 record 不可继续写入。
- SQLite 变更使用同一个 `SqliteDatabaseParticipant` 的未提交事务。中途 `ApplyDecidedWritesAsync` 只刷新已决定写入，不绕过最终 detached 校验；数据库或后续参与者失败由事务回滚恢复槽位、世界行、属性、数量和邮件领取标记。
- 独立物品移动保留 UID；材料部分 Take、跨容器 Put、兼容合并和 Delete 保持数量守恒，不兼容 Snapshot 由 `CanStackWith` 拒绝合并。
- 邮件附件成为真实 custody：发送侧接收 detached record，领取侧校验收件人、可见性、期限、唯一未领取附件和世界行所有权，再在同一事务发行 record。GM 邮件走 Create 后托管，领取失败可回滚重试。
- 最终 Persistence 修复统一了普通容器与邮件世界行的时装颜色读取：先按 `Int64` 读取，再对两个颜色执行 `0..65535` 范围校验，越界稳定抛出 `InvalidDataException`。测试覆盖 `-1`、`65536`、`long.MaxValue`、合法边界和邮件领取回滚重试。

### 调用方与状态完整性

- 换装、仓储、整理、拆合、消耗、商店、奖励、拾取、邮件、强化/附魔/锻造等装备变更均迁移到 Get/Take/Put/Update/Delete 语义；没有发现绕过统一生命周期重新写入 Core 的业务路径。
- 装备实例持有当前位置引用并通过 Update 写回；引用失效时不会按 UID 追踪并误改新占用。宠物与蛋的状态通过同一 item facet 更新，完整字段、未知字段及在线计时的整数落库路径没有第二份可变真源。
- 角色/账号载入、协议投影和 wire 编码从 Snapshot 映射，协议 golden 未被改写。现有容量、固定槽、准入、账号归属、锁定、期限及特例守卫仍在对应规则或容器边界执行。

### 自动化证据

- `artifacts/item-container-lifecycle/solution-final-build.log`：解决方案构建成功，0 warning、0 error。
- `artifacts/item-container-lifecycle/solution-final-tests.json` 与 `test-results/*-final.trx`：八个测试项目全部 exit 0，共 5644 passed、0 failed、2 skipped（5646 total）。分项为 Persistence 458、Application 2837、Protocol 1247、Infrastructure 768 passed/2 skipped、Host 118、HttpApi 50、Networking 86、Pvf 80。
- Infrastructure 的两个 skip 仅验证“找不到 PVF 档案”时的报错；本次真实 PVF 可达，skip 原因为 `An archive is reachable from this working tree.`，与真实 PVF 用例互斥，不构成本任务覆盖缺口。
- `Persistence.Tests/Inventory/Lifecycle` 的真实 SQLite 用例覆盖 Take/Put/Update/Delete、UID 与未知字段、旧引用失效、伪造/重复接收、Flush 后提交拒绝、回滚、材料拆合、时装/宠物状态和邮件 custody。最终 Persistence 458/458 包含新增的六个邮件损坏颜色边界例。
- `git diff --check` 在冻结目标上无输出。
- 提交前对 `artifacts/item-container-lifecycle/final-whitespace-paths.txt` 所列 25 个新增或被识别为 rename 的文件统一 LF 并清除尾空白；控制器逐文件比较确认非空白字符完全相同，本次复核也确认所有清单路径均无 CR、无尾空白且完整 staged `git diff --check` 无输出。受影响 C# 的 raw multiline 仅为 SQLite SQL 和测试 DDL，换行归一仍作为 SQL 空白处理，不改变程序语义。

## Findings

无阻断 finding；无待补 prerequisite。

## 范围边界

本任务没有新增玩法、报文布局或 `ClientVerified` 要求。本审查只确认 CurrentImplementation 与自动化回归满足当前 TaskContract；不把自动化结果表述为 A21 实机验收。
