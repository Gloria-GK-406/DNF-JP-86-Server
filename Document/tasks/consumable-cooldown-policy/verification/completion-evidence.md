---
kind: rks-verification
decision: manual_required
---

# 消耗品冷却重构验证

本次复核目标为 `task/consumable-cooldown-policy` 工作区，基线 `e79884827631be73e3da2651b06345dd58b50624`，契约修订 3。 [独立代码评审](../reviews/change-review.md)

## 自动验证

最终工作区执行 `dotnet test Server/86JP.Server.slnx --filter <本次改动相关测试> --nologo -m:2`，退出码 0。定向测试 **790 项通过**，包括 Infrastructure 86、Application 579、Persistence 23、Protocol 89、Networking 4、Host 6、DbMigrate 2、Dfo.Pvf 1。排除了在主干复现的 `PvfTeleportConsumableDefinitionSourceTests.Real_potion_selection_destinations_and_Shonan_gate_are_typed`。作为补充，`python -B Tool/ProjectReferences/check.py` 检查 13 个生产项目和 32 条引用，退出码 0。最终 Git 差异核查通过。

| 契约 | 已验证证据 |
|---|---|
| AC-1 | 真实 dev PVF 检查人偶 2600053 和 2600047、重复声明 2680327 为 5 秒和 10005034 为 24 小时、带标记瞬移 10151895。夹具覆盖缺省、000、100、111、零时长和错误声明。 |
| AC-2、AC-3 | 普通使用命令验证无标记同组道具继续消费。合规的 99 组成员共享权威键，其他 PVF 组按物品持久化。瞬移、遗忘之地及错误入口仍按已接入动作规则结算。 |
| AC-4 | 既有疲劳记录和新到期表分别验证跨 06:00、服务容器重建、原截止、日次数和重放。验证 0x00AC 分包、剩余秒数、客户端整数上限及 v48 → v49 保留历史。 |
| AC-5 | 模拟 SQL 失败、提交失败、原请求重放、截止交界和会话替换。核对库存、作用结果、冷却和发生时点。 |
| AC-6 | 逐道具索引仍保留候选状态。实施覆盖已接入入口，A21 行为按人工测试另行记载。 |

真实模板校验用 dev `Script.pvf`，SHA-256 为 `a97132dda0970b3e0ce7c3de8052acf0be2664f52e26c819b1d8cb6d427cfcd2`。全量 dev 调查期间，同一 PVF 的任务分支和基线各出现 17 条同名 Infrastructure 失败。包括 2600014 最低等级测试期望 60，两个版本均解析为 0。也在基线复现了选角记录中的额外 0x007F。早期全量采样有超时和在后续已调整的夹具断言，最终结论以定向复验为准。

## 未验证条件

A21 实机仍需观察人偶跨场、标记瞬移与 99 组恢复显示，并复核重复请求后的图标和库存。进程重启及跨 06:00 的本轮证据来自隔离夹具与服务容器重建，实机流程按 [验收清单](../user-test.md) 保留。


## 提交前复验

在主干 `4341e68c5fbd5d3dfa81f65d099b00ae712f6a9c` 上保留瞬移位置变化回执和副职业商店访问清理，并应用冷却效果的实际时长。使用同一 dev PVF 重跑相关定向测试，389 项通过，退出码 0。测试涵盖瞬移回滚及重放、普通消耗品、疲劳、旧库迁移和内容边界。用户确认在合并后实际运行时完成 A21 人工验收。
