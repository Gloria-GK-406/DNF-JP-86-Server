---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan：装备普通强化

## Planning Choice

客户端回包、70 端惩罚规则和 A21 PVF 表需要先比对，才能定下正确的事务和状态投影。采用逐步细化的 Task Unit：先收紧证据和设计，再写领域结算，最后接协议和集中验收包。后续单元的具体报文字段与规则输入取决于前一单元的结论，不预写猜测。

## Approach and Shared Decisions

- 服务端从角色拥有的现有装备实例出发，按业务槽位定位并验证模板、身份与可强化性；`InventoryItemCoreState.EnchantUpgradeCount` 是当前持久化和投影中的等级字段，其他实例字段必须保留。材料、金币和保护券在同一命令事务中结算。请求中名称只参与可证实的协议校验，不作为资产权威。
- 内容侧从本次 A21 `Script.pvf` 读取普通强化配置；70 端 `WongWork::CItemUpgrade` 与上游 `InventoryItemUpgradeService` 用于查规则分支及版本差异。先确认 A21 客户端 `CMD 0x0050` 的接收读取路径与必要的物品/金币刷新，再定出站结构。
- 普通强化只消费自己的模式；增幅与锻造可共用解包基础，但业务必须分别保持未实现或交由后续 Spec。随机判定应能用确定输入验证边界，拒绝路径在触碰资产之前结束。对破坏产物先证明容量与实例去向，再允许提交。
- 沿用现有 `Application` 领域服务、`Infrastructure` PVF 内容源、`Protocol` 消息切片及 `Persistence` 事务接缝，不让协议 handler 直接改仓库行；所有普通强化结果经命令事务确认后投递。

## Work and Dependencies

| Work | Delivered outcome and boundary | Dependencies | Covers |
|---|---|---|---|
| T-001 | 70 ELF、上游、A21 客户端和真实 PVF 的普通强化证据对照；查明请求、响应、费用、概率、保护和惩罚分支，形成可实施设计及未知列表。 | 已接受基线；不依赖其他子 Spec。 | RQ-001、RQ-002、RQ-003、RQ-005 |
| T-002 | PVF 规则输入与普通强化领域结算，覆盖目标准入、可控结果、资产原子性和保存失败；用针对性测试证明实例与数量守恒。 | T-001 已确定会影响破坏/扣费的语义。 | RQ-002、RQ-003、RQ-004 |
| T-003 | A21 `0x0050` 入站、结果回包、库存/金币更新和必要通知；验证编码、Host 集成与重登，并交付 Human Test List。 | T-001 报文证据与 T-002 结果契约。 | RQ-005、RQ-006、整项集成 |

这些是整体工作包；实施控制器只为下一个可执行边界建立详细 Task Unit。T-001 若发现 A21 与 70/上游在关键规则上冲突，以客户端/PVF 证据裁定，并回写方案；无法安全裁定时停在证据门，不让 T-002 用猜测执行破坏或扣费。

## Verification

T-001 的每个主要结论应可追到具体文件/地址和证据等级。T-002 用确定性成功与失败输入检查装备、金币、材料、保护券和产物提交前后状态，包括拒绝、重复、保存失败。T-003 检查请求与响应字节、物品及金币刷新顺序、真实 SQLite 重载与真实 PVF 的可读性。最终对照 Spec 的 AC-001—AC-006 做完整结果复审和新鲜验证；用户 GUI 验收保留为待执行。

## Next Planning Boundary

先建立 T-001 的详细 Task Unit：只读分析 70 `Dispatcher_UpgradeItem`/`CItemUpgrade`、上游强化模块、A21 `0x0050` 构包与响应读取、A21 `etc/upgrade.etc`，输出有证据等级的设计决策。T-002 的具体规则接口和惩罚枚举待此结果确定后规划。
