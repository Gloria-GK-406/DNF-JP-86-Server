---
kind: rks-review
verdict: approved
---

# 公会成员上下线代码审查

审查范围为 `97b10c76ea460d33d2754ee9de2efb7a9f91b4d0` 至当前工作树中的选角、公会出站协议、共享清理及对应测试。依据[契约第 3 版](../TaskContract.md)、[规格](../spec.md)、[实施路线](../routing-decision.md)和 `reviewing-work` 的完整代码审查与证据保留准则。

- 上线在 `EnterWorld` 和会话发布之后登记。编码器写状态和频道两个单字节，再写长度为 `i32` 的角色名原始字节，符合 A21 的消费顺序。频道上限与游戏连接入口一致，CH255 可发出 `[1, 255]`。[选角发布](../../../../../../Server/src/Dfo.Server.Protocol/Game/SelectCharacter/GameSelectCharacterCommand.cs#L400) · [编码器](../../../../../../Server/src/Dfo.Server.Protocol/Game/GuildPresence/GuildPresenceSlice.cs#L38) · [A21 静态证据](../../../../../../protocol/game/server-to-client/NOTI-0x0040.md)
- 回选、断线和选角替换经过共享清理。仅为曾入场的精确会话采集离线计划，精确释放后投影。重复清理查不到旧会话，后继会话已入场时丢弃旧离线。换角阶段先登记原角色离开，再登记入场角色上线，发件箱交付顺序保留。[共享清理](../../../../../../Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs#L232) · [投影当前性](../../../../../../Server/src/Dfo.Server.Protocol/Game/GuildPresence/GuildPresenceSlice.cs#L16)
- 持久成员名单通过同公会连接查询得到，排除本人后交给现有通知机制。受众解析要求当前在世界，发送前复核角色与连接。仅选中、其他公会及已离开的接收者不会收到这一轮的投影。[持久名单](../../../../../../Server/src/Dfo.Server.Persistence/Guilds/SqliteGuildDomainStore.cs#L50) · [受众解析](../../../../../../Server/src/Dfo.Server.Application/Notifications/NotificationAudienceResolver.cs#L83) · [投递前复核](../../../../../../Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs#L174)
- 初审发现公会读取异常可在只读工作单元记录为请求故障，以及有效 CH255 被上线条件漏掉。已复审修复。读取异常在单元操作内处理并返回空名单，取消照常传播。退出端捕获可丢弃的名单读取异常后继续完成精确释放。[选角读取](../../../../../../Server/src/Dfo.Server.Protocol/Game/SelectCharacter/GameSelectCharacterCommand.cs#L842) · [清理读取](../../../../../../Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs#L249)

本审查确认服务端代码路径、报文与测试断言和规格一致。独立运行 `GuildPresenceTests` 时，VSTest 等待 testhost 握手超时，未取得测试判定。线路与 A21 双端界面按本期独立验收结论记录。
