# TASK-003 — 地下城绑定 PartyKey 与当前成员资格

## Task Unit Contract

- Revision: 1.
- Outcome: RunId 绑定稳定 PartyKey；PartyService 返回当前成员/队长作为唯一组队真源。副本可以保留每人位置、加载、奖励进度，但不可用入场时冻结名单授权角色动作或广播。成员离队后原运行继续，图外后来加入的成员不得进入既有运行。
- Owned boundary: DungeonRunState/IDungeonRunDomainService、DungeonRunDomainService、DungeonVisitDomainService、DungeonPartyRecipients、应用层出入场协调及相关测试。本单元不改变 Cell 内容生成规则或掉落结果兑现，留到 T-004。
- Checks: leader 离队而未先 End 时仍只有继任者被认作成员；图外新成员不能借 Key；原 RunId、Cell 保持；旧连接与已离队者不收运行通知；虚拟单人不分叉。
- Depends on: T-001 Key 查询和 T-002 客户端新旧代际通知语义。

## Detailed Plan

1. 为 `DungeonRunState` 和聚合增加稳定 `PartyKey`，进入时从 PartyService 取得并校验入场队列。用入场队列完成当次初始 Cell 准备，但不再把它当成员资格或后续 Cell 的规则输入。
2. 聚合将每角色进度/物理进入索引与队伍成员分开。运行查询和行为授权取 PartyService.FindByKey 的当前成员，并和实际在运行的索引相交；旧索引用于清理、进度，不单独授予资格。动态返回当前 leader/slot，禁止新成员因只在 PartyService 中而自动进入旧 RunId。
3. 通知收件人以绑定 Key 查询当前队伍，再以运行索引、当前 SelectedSession 和本 Run 的连接代际限定。检查 `DungeonEntryChange`、Cell 事件、奖励广播等所有发包出口。
4. 退图/离队/断开连接路径保证清理运行索引和队伍成员的顺序可容错；最后成员离开时清理孤儿运行。更新必要的用例；对仅依靠无 PartyService 的旧单人测试提供明确测试兼容，不让 Host 采用该回退路径。
5. 运行领域、协议与整体构建，整理 T-004 Cell 输入/专属结果的明确调用点。

## Result

- State: code complete, 2026-09-17; A21 运行效果留待 T-005 实机。`DungeonRunState.PartyKey` 绑定稳定队伍，聚合的当前成员镜像从 PartyService.FindByKey 读取，并与已进入该 RunId 的位置/进度索引相交；旧入场队列仅作为初始资源及个人进度依据，不能独立授权动作。
- `DungeonPartyRecipients` 从 RunId 取 Key 再查当前队伍，额外限定当前 SelectedSession、运行索引和投递登记。主动退队命令清理自己的 RunId 索引，协议事件移除旧投递登记；连接清理由原有顺序先 End 后退队。
- Evidence: 队长退队后原 RunId 继续、图外后来入队者不获资格、退队者无运行视图、退队命令仅清本人；领域/协议/Host 相关测试通过。实际双客户端行为尚未验证。
- Ownership note: Run 仍保存每角色进度及进入索引，不再将其当作队伍成员真源；该索引只表达“是否实际进入此 RunId”，防止队伍后续成员凭同 Key 自动进入。
