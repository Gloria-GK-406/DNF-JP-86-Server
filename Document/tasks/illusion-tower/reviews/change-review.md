---
kind: rks-review
verdict: approved
---

# 迷妄之塔完整结果评审

## 评审对象

- 目标：基线 `762d0cb4` 之上的迷妄之塔完整未提交实现，包括 TASK-01 至 TASK-07、协议知识库、实施结果、human test list，以及完整结果评审中 R1–R3 的修正。
- 工作区：`DNF-JP-86-Server/.worktrees/warehouse-storage`，分支 `task/warehouse-storage`。
- 判据：[TaskContract](../TaskContract.md)、[Specification](../spec.md)、[Plan](../plan.md)，以及 `reviewing-work` 的完整 Code 与 Deliverable 判据。
- 方法：独立读取生产实现、公共调用路径、失败处理、测试源码、协议记录和交付文档；单元结果与验证记录只作为证据入口，没有替代实际代码检查。未重复执行由后续 verification gate 负责的新鲜全解决方案验证。

## 结论

当前评审范围内没有未解决的阻塞缺陷。实现满足独立塔领域、真实 PVF 内容、事务入场与奖励、逐层推进、结束与返回、队伍变更、协议接入及集中实机验收准备的约束，文档也没有把 ClientStatic、70Reference 或 CurrentImplementation 提升为 ClientVerified。

本批准只通过完整结果 review gate。它不代替下一阶段的新鲜全解决方案验证，也不把待用户执行的 A21 实机项目标为通过。

## 成功条件核对

| 条件 | 独立评审结论 | 决定性证据 |
|---|---|---|
| SC-1 | 通过 | `IllusionTowerDomainService → IllusionTowerDgn → TowerRoom` 独立持有挑战与房间事实；普通副本只在公共入口和互斥能力处接入，没有以 `DungeonCell`、迷宫推进或公共塔框架承载塔状态。架构测试覆盖新增分层。 |
| SC-2 | 通过 | `PvfIllusionTowerContentSource` 读取 11001 的 15 层、普通怪、固定/随机 APC；规则测试覆盖纯 APC 与混合层的敌对完成条件。TASK-07 的公共入口测试使用生产 PVF 内容，严格遍历 15 张预期地图并确认每层演员非空。 |
| SC-3 | 通过 | 公共 0010 分流在普通 maze 解析之前进入塔；等级、券、成员准备和房间创建位于事务边界，失败测试覆盖不扣券、不发布半成品运行和普通副本互斥。成功组队入场在提交后消费所有 exact 成员的 entry intent，失败路径保留 intent。 |
| SC-4 | 通过 | 0025、009F mode 1、0027、009F mode 2/002D 驱动 Loading→Ready→Fighting→Cleared→NextLevel；actor UID、RoomVersion、RunId 与 exact session 防止重复或迟到消息跨层生效。真实 PVF 公共流程完成 15 层，并验证未清场推进不改变层或版本。 |
| SC-5 | 通过 | 成功、全灭、主动离开、GiveUp、断线与到期返回均通过挑战事实结束或清理；实际完成层与奖励计层分开保存。旧 sweep 已捕获的 context 在 0084 返回并重新 0010 入场后不能改变新运行、资产或消息。 |
| SC-6 | 通过 | 击杀与结算奖励通过真实事务写入经验、金币、物品、任务进度和一次性 receipt；SQL 故障、外层回滚、满包、重复击杀/结算均有回归。已提交通知首发失败只重发同一个已构造批次一次，重发前复核 exact session、RunId、RoomVersion 与 receipt，不重新执行奖励；持续失败在门外关闭并由真实 cleanup 收敛。 |
| SC-7 | 通过 | 008E–0092、009F 的 decoder/encoder、handler、CMD 与 `ProtocolModule` 注册已接到领域行为；字节向量、0092 真实注册 encoder 的 0/1 向量及塔帧 TCP 集成存在。协议人工区保留未知字段和证据等级，生成目录经修正后的类型限定常量检查器覆盖。 |
| SC-8 | 通过 review gate | 相关 Party、普通副本、物品、经验、任务、宠物投影、仓库限制和架构回归已有有界证据；[human test list](../human-test-list.md) 集中列出 A21 首层、15 层、结算、队伍、返回与普通副本验收，全部仍明确为待验收。最终全解决方案 Release 验证属于下一 verification gate。 |

## 评审发现的修正复核

| 发现 | 处置 | 复核结论 |
|---|---|---|
| R1：队伍附带成员成功入塔后仍保留 `DungeonEntryIntent` | 成功提交后、任何 refresh/发送前消费所有仍为 exact 的非 requester intent；失败前不消费 | 通过。真实双成员组合测试覆盖 requester 原路径、附带成员消费及随后踢出回城。 |
| R2：协议目录检查器用未限定同名常量，可能把角色经验 0025 映射成宠物经验 0066 | 常量按声明类型保存，拒绝歧义短名、错误限定名和实例属性；增加乱序同名常量自测，并显式登记动态库存类型 | 通过。自测和仓库检查均成功，目录不再依赖输入文件顺序或简单名称碰撞。此检查只证明源码覆盖映射，不证明 wire 语义。 |
| R3：已提交奖励发送失败后没有生产调用者执行重投 | `ProjectActionAsync` 对每个接收者构造一次批次并最多发送两次；第二次仍在原 run/account gate 内且通过 exact/receipt 校验 | 通过。非末层击杀、最终结算、持续失败 cleanup 和替代会话四类真实 DI/SQLite 测试覆盖资产只提交一次及旧批次不误投。 |

## 代码与失败边界

- 塔与 Party 的非原子边界先提交持久塔事务，再以 `CancellationToken.None` 立即执行已验证的内存 Party 变更；可预见拒绝均发生在塔事务前。Party 包装器写后异常不会伪装成未修改，而是在释放 run/account gate 后隔离仍为 exact 的连接。
- 协调锁顺序固定为 run 投影 gate，再按 AccountId 排序取得账号 gate。普通 Party 路径在账号 gate 内发现塔归属时先释放再重路由，没有形成 account→run 的反向获取。
- 返回、断线、投影失败和延迟操作都携带或重新核对 selected session、RunId 与 RoomVersion；替代会话和新挑战不会被旧清理、旧通知或旧计时器污染。
- 奖励候选与实际交付分开记录。满包未交付不会被写成已发放，也没有引入无证据的邮件补偿、排行榜加成、每日赠券或其他塔的公共框架。

## 交付物与证据边界

[实施结果](../spec-result.md)、[实现补证](../reward-and-projection-evidence.md)、协议人工记录和各 TASK 验证与实现一致。009F 未命名的 8 个 `u16`、008F 非零物品行、A21 按钮文案、客户端画面及时序没有被猜成已确认语义；70 的奖励、继续条件与返回延时均保持 70Reference 标识。

仍需下一阶段处理的非阻塞边界：

- 使用当前 PVF 路径执行新鲜全解决方案 Release 验证，并记录零失败、零意外跳过或明确基线差异。
- 由用户按 human test list 完成 A21 实机验收后，才能把相应首层放行、跨层画面、结算界面、返回按钮和队伍可见行为提升为 ClientVerified。
- 已登记的 D-001 保留为性能技术债：部分塔通知在同局账号 gate 内发送，慢连接可能延迟同局后续动作或重连；当前正确性由有界重试、exact 检查与失败关闭保护。
