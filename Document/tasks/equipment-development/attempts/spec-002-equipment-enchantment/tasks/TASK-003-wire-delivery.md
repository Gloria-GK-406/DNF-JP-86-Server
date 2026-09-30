# TASK-003 — `0x0110` 协议投递与人测清单

## Task Unit Contract

- Revision: 1
- Outcome and references: A21 宝珠附魔请求进入已验证领域事务；已提交后发静态接收器可读的 ACK、目标和宝珠槽刷新，拒绝与重放不重复扣除，形成 Human Test List。覆盖 Spec RQ-004/RQ-005 与 AC-004/AC-005。
- Owned boundary: `Server/src/Dfo.Server.Protocol/Game/EnchantByBead`、消息身份/DI、相应协议和 SQLite 测试、本子项 `Document`；不碰宠物结算、附魔师摊位 `0x025A` 或客户端二进制。
- Required checks: 六字节入站、成功 `01,list,i16 slot`、错误 `00,reason` 的字节级测试；异常列表/负槽/宠物目标拒绝；命令提交失败不发成功；同会话同序列同请求重播不再扣珠，冲突请求拒绝；跨会话不回放；穿戴/主背包物品投影与重登一致。
- Dependencies: T-001 静态证据与 T-002 领域 receipt 已完成；附魔师摊位范围仍待用户答复，不在本单元预支实现。

## Detailed Plan

1. 先写 decoder/encoder 字节测试，失败断言；将 opcode `0x0110` 对应网络消息和编码器接入协议模块。
2. 以 `ICommandBus` 的 transactional、account/online exclusive 命令调用领域服务；request 只传容器/槽位，不传卡 ID。给 handler 连接级有界 seq+fingerprint 缓存，提交成功后记录，当前角色切换则清空。
3. 成功投递经 NotificationBus 向同角色当前连接投影 ACK 与两个受影响槽，清空/剩余均从 receipt 来，绝不重新猜库存；错误只投递错误 ACK，宠物分支拒绝且不进入装备领域。
4. 用真实 SQLite fixture 检验一次成功、重放、序列冲突、提交失败与通知顺序；跑定向和相关回归，写明确可复现样本、抓帧位置和待客户端验收项。用户范围答复到来时调整附魔 Spec 边界。

## Result

- `0x0110` 六字节解码、四字节成功 / 两字节错误 ACK 编码与注册已完成，字节级定向测试 5/5 通过；这些是 ClientStatic + UpstreamReference 支持的实现，非实帧确认。
- transactional/account+online exclusive 命令在提交后发布目标与宝珠 `0x000E` 槽刷新及 ACK。网络入口对所有 Game 帧记录到达序数和计数连续性；附魔连接级缓存只为近期同序列同请求补投当前槽状态，超过 128 帧的旧包拒绝，完整计数回绕的合法新包可前进。该通知不作后台重试，补投与新附魔同受连接级序列闸排序；SQLite handler 测试现为 12/12，涵盖部分投递失败、受控发送交错、长间隔其他 opcode、缓存淘汰、回绕、提交失败重试和宠物隔离；网络计数单测 2/2。错误码使用上游 `0x11/0x13/0x17` 对应 A21 静态分支，但提示/实帧未知。
- `Human-Test-List.md` 已标注待用户 GUI 验收。无 PVF Application 2036 通过/171 跳过；真实 PVF Infrastructure 404 通过/2 跳过；Protocol 1116 通过/60 跳过；构建 0 警告。真实 PVF Application 全量 2215 通过/3 失败，其中两项既存宠物路径失败，修理用例单独重跑通过。完整审查和范围确认仍待本 Spec 后续完成。
