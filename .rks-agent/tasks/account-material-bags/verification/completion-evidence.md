---
kind: rks-verification
decision: pass
---

# Completion verification：账号级晶体与品级灵魂背包

## Claim

TaskContract revision 1 的 SC-1～SC-7 已由当前任务分支结果满足，可以形成本地验收提交。
本次授权不包含合并、部署、启动服务或清理工作区；A21 实机效果留待用户在合并启动后验收。

## Success-condition evidence

| Conditions | Decisive evidence |
|---|---|
| SC-1 | `AccountMaterialBagTests` 与持久化入场测试证明同一 `AccountId` 复用同一逻辑 Bag、两个角色读取同一账号记录，另一账号保持隔离。 |
| SC-2 | NPC/便携分解及非分解教程发放测试证明六种晶体和五种品级灵魂只增加账号余额，不创建角色 `item_slots`；容器最终写边界拒绝 direct-slot 旁路。 |
| SC-3 | 宇宙灵魂 `3257..3260` 不在账号材料目录，分解回归证明其继续按角色普通材料的容量、堆叠和槽位规则结算。 |
| SC-4 | 账号写入、角色材料写入和便携工具结算的失败注入测试证明装备、工具、角色材料与账号材料共同提交或共同回滚。 |
| SC-5 | 入场 `0x000D` 与增量 `0x000E` 测试覆盖 `354..364` 的固定映射和绝对数量；分解 ACK 继续表达本次产量。 |
| SC-6 | `Document/AccountMaterialBag.md` 与 `NOTI-0x000D/0x000E` 明确区分 A21 线路/PVF 证据、本服账号级玩法策略、宇宙灵魂例外及待实机验证边界。 |
| SC-7 | 独立完整结果复审 verdict 为 `approved`；生产 `AddAsync` 到货统一经 `IItemGrantDomainService`，十一项映射只定义于 `AccountMaterialCatalog`；构建、完整测试及 diff 检查通过。 |

## Fresh checks

- `dotnet build Server/86JP.Server.slnx --no-restore`：0 warning、0 error。
- 设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` 后，
  `dotnet test Server/86JP.Server.slnx --no-build -m:1`：3542 passed、0 failed、2 skipped。
- 两项 skip 均为在已提供 PVF 时跳过的“缺失归档应报错”负例，不是功能缺口。
- 账号材料/分解/教程奖励聚焦测试：Application 35/35；相关协议测试 216/216。
- `git diff --check`：通过。
- 独立审查：`reviews/change-review.md` 第二轮 `approved`，首轮 F-001/F-002 均关闭。

## Scope boundary

- 不迁移、不合并也不自动删除历史角色材料栏中的错误晶体/品级灵魂；用户测试前自行清理。
- 购买/开盒等尚未建立账号材料业务语义的 direct-slot planner 若意外产出这十一种资源，会显式失败并回滚，不会写入角色槽。后续接入必须复用 `IItemGrantDomainService`。
- 宇宙灵魂仍是角色普通材料。
- `0x000E` 分解后的实际客户端刷新效果尚待下一阶段 A21 实机验收；本次只声明实现、编码、顺序和自动化验收完成。
