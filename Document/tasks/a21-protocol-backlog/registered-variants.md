# 已接入口的覆盖边界

这张表登记逐号差集中容易漏掉的操作。已有 decoder 或 encoder 的编号也可能缺少一个玩家可见分支。

| Game 身份 | 已有行为 | 可继续验证的缺口 |
|---|---|---|
| CMD 0x000A／0x000B | 常规组队和角色交易使用同号的不同子类型。交易 type 1 已有 A21 双端成交证据 | 逐类覆盖各自邀请、应答、断线，保持交易 #41 的已验范围。构帧补证发现四个额外调用均为 0x000B。 [邀请](../../../protocol/game/client-to-server/0x000A.md) · [应答](../../../protocol/game/client-to-server/0x000B.md) |
| CMD 0x0010、0x001F、0x0022、0x0308 | 普通、剧情、循环等副本经选择与任务各自编排。主动深渊有独立准入，安图恩攻坚有专用状态 | 组队循环图队员任务及奖励见 [#296](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/296)。随机深渊默认拒绝见 [#140](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/140)。普通／觉醒安图恩仍由 [#78](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/78) 跟踪 |
| CMD 0x002C | 常用道具、药剂、图纸等按现有物品服务结算 | 检查消耗品按类型的实际效果与失败边界，见 [#374](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/374) 和 [技能冷却规则协议](../../../protocol/game/client-to-server/0x002C.md) |
| CMD 0x0040／0x0041 | 游戏内 Cera 商城购物有独立处理。0x0041 网页商城票据有 decoder，固定返回不可用 | 网页票据是独立功能范围，实际客户端入口与当前运营需要仍待确认。 [网页票据处理](../../../Server/src/Dfo.Server.Protocol/Game/GenCeraTicket/GenCeraTicketSlice.cs#L36) |
| CMD 0x01A9 | 物品链接聊天当前读到入口后返回拒绝 | 需要 A21 构包、现用发送/接收范围、来源物品展示权限，以及后续查看链路。组队、公会、私聊的标准消息已有各自路径。 [切片消息](../../../Server/src/Dfo.Server.Protocol/Game/SendChat/Messages.cs#L25) · [#61](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/61) |
| CMD 0x01B3 | 末字节标明来源。公会来源 2 已有双端实机，光环来源 1 明确拒绝 | [#297](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/297) 覆盖光环传送。0x01B4、NOTI 0x017A 是否属于该流程按真实事件顺序验证。 [A21 来源分支](../../../protocol/game/client-to-server/0x01B3.md) |
| CMD 0x01BA | 选角和城镇查询都返回真实冒险团参数，佣兵列表计数固定为 0 | 派遣、召回及个人支援兵为 [#89](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/89) 的待核范围。公会租用为 [#389](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/389)。 [查询契约](../../../protocol/game/client-to-server/0x01BA.md) |
| CMD 0x0204 | 同号按 action 和 PVF 数据分流，部分动作已有 A21 证据并已收口 | [逐动作矩阵](../consumable-actions-371/action-matrix.md) 记录每个动作的道具、替代请求和最小验证。其余使用与券类走 [#371](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/371)、[#372](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/372)、[#373](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/373) |
| CMD 0x029E | 主线已接 op 0 编组和 op 1 团长转移，在共用团状态更新上投影 | A21 该 CMD 响应处理为空，业务验证聚焦实际团长、组号和名单；客户端转移、组 7–10 的完整实机仍归 #79。 [命令分流](../../../Server/src/Dfo.Server.Protocol/Game/AntonRaid/AntonRaidCommand.cs#L203) |
| CMD 0x03CF／0x03D0，NOTI 0x03C1 | 幸运星武器租赁、金币购星、续租及面板同步已实现。2026-10-20 用户确认本轮实机验收通过 | CMD 0x0372 的旧页署名误引租赁，A21 对应活动副本清房。历史 PCROOM 和租赁活动仍按各自入口核验。 [租赁验收](../../LuckyStarRental.md) · [旧号校正](../../../protocol/game/client-to-server/0x0372.md) |
| CMD 0x03E5–0x03E8，NOTI 0x03EB | 收集箱选择、放入、取出、期限、持久化与上线投影已有独立实现 | CMD 0x0389 的旧字段误引收集箱。装扮衣柜的客户端入口和资源单独取证，继承 #56。 [收集箱交付](../../CollectionBox-Implementation-20261019.md) · [旧号校正](../../../protocol/game/client-to-server/0x0389.md) |
