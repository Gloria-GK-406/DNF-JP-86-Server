# 跨连接顺序要求检索（附录 D）

基准：HEAD `b76504bb`。范围：`Server/src/Dfo.Server.Protocol` 与 `Server/src/Dfo.Server.Application`。只读检索。逐命中处置清单见同目录 `xo-dispositions.md`，由一次性脚本生成，生成时自动核对每个命中行号都有分类。

## 检索

| 检索 | 口径 | 命中 |
|---|---|---:|
| `first` | 注释内 | 145 |
| `then` | 注释内 | 105 |
| `after the` | 整行 | 72 |
| `before` | 注释内 | 204 |
| 合计（按关键词） | | 526 |
| 去重（文件, 行号） | | 498 |
| `ProjectAsync(` 调用点 | 排除定义、接口与文档注释 | 95 |
| `Peers` | 整行，不区分大小写（结果审查后补跑） | 29 |
| `must precede` | 同上 | 4 |
| `ordering` | 同上 | 13 |

另有三类检索：
- 显式多收件人发送：`GameExplicitMessageDelivery|SendBatchAsync|SendExplicitAsync|SendToExactAsync`；
- 排序模式：`OrderBy\(.*requester.*\? ?[01]`，XO-3 与 XO-4 由此发现，它们没有注释；
- `foreach .* Where\(.* != (session|requester|actor)`。

加法：
- 按关键词：145 ＋ 105 ＋ 72 ＋ 204 ＝ 526，共 4 项。
- 按分类：T 183 ＋ H 46 ＋ R 54 ＋ I 27 ＋ W 125 ＋ S 53 ＋ N 2 ＋ X 4 ＋ V 4 ＝ 498，共 9 项。
- 两者相差 526 − 498 ＝ 28，这 28 次是同一行命中多个关键词时的重复计数。
- ProjectAsync：95 行逐行处置，结果为 是 3（XO-5；XO-6、XO-7 为保守列入）＋ 不是 92 ＝ 95。不是的 92 行细分为：单收件人 57、无先后语义的一次多人投影 13、「先操作者本人」的写法 5、同名的非 IEventService 方法 17，合计 92。

补跑三词的处置：
- `Peers` 共 29 行。其中 3 行已登记或已处置：DungeonEntryProjectionDelivery.cs:203 是同一批次内的帧顺序，S；LeaveDungeonSlice.cs:283 是 XO-2；SupplementalSynchronizationMessages.cs:226 是 XO-5。另外 TownAreaProjectionDelivery.cs 中 23 行、TownReturnProjection.cs 中 3 行都是变量名（`peers`、`peerSessions`、`preExistingPeers`、`visiblePeers`）。其中 :204-210、:575-588 是「先向同伴发到达、再向本人发同伴」，没有注释或测试要求这个先后，按附录 D「允许乱序的写法」处理。
- `must precede` 共 4 行。ReplayStateProjectionDelivery.cs:73、RentalSlice.cs:153、ItemSocketsSlice.cs:155 都是同连接帧先后，S；SelectCharacterPhaseCommands.cs:49 是服务器内部执行次序，T。
- `ordering` 共 13 行，都不是跨连接先后：BossTower 与 IllusionTower 的 PartyCoordination 说的是租约次序；CreateCharacterSlice.cs:91 是同连接帧序；两个遥测 handler 讲的是入站容错；ResultScreenSlice、HiddenCharacterInfoSlice 是实机记录；DungeonPartyWipeService.cs:82 是内部次序；Application 中 5 行是排序键。

范围：只检索了 Protocol 与 Application 两个项目。Networking 是传输层，不认识协议；HttpApi 与 GmWeb 只回给请求方，不涉及多连接投递。所以这三个项目不在范围内。

口径的局限：归入 S（同连接帧先后）、T（服务器内部执行次序）、W（单帧布局）的行，只读了命中行本身，没有回读上下文。

## 结论

| ID | 要求 | 今天的位置 | 今天守着它的测试 | 处置 |
|---|---|---|---|---|
| XO-1 | 组队进本：跟随者的进入消息先于请求者 | `Protocol/Game/DungeonEntry/DungeonEntryProjectionDelivery.cs:353-376` | 间接守着：`PartySelectDungeonHandlerTests` 的 `A_leader_delivery_failure_isolates_the_member_that_already_received_start_map`、`A_secondary_recipient_delivery_failure_aborts_entry_before_anyone_leaves_town` | 有序条目 |
| XO-2 | 离开副本：同伴先看到决定，领队后收到 ACK | `Protocol/Game/DungeonClear/LeaveDungeonSlice.cs:283-309` | 无 | 有序条目 |
| XO-3 | 领主之塔进入：各成员批次先于请求者 | `Protocol/Game/BossTowerProjectionDelivery.cs:175`（按 `Session == requester ? 1 : 0` 排序） | 无 | 有序条目 |
| XO-4 | 幻影塔进入：同 XO-3 | `Protocol/Game/IllusionTowerProjectionDelivery.cs:171` | 无 | 有序条目 |
| XO-5 | 清理通告：先发所有者，再发城镇同伴，最后发队伍投影 | `Protocol/Game/StateSynchronization/SupplementalSynchronizationMessages.cs:222-276` | `CleanupNotificationDeliveryTests` 的 `The_owner_is_told_before_the_people_around_them`、`Existing_owner_and_town_notices_finish_before_party_projection` | 有序条目，并与 DR-10（必达失败即停止）互相引用 |
| XO-6 | 队伍状态按阶段跨收件人推进：每一阶段全员送达后，才进入下一阶段 | `Protocol/Game/Party/PartyNotificationSlice.cs:798-842`（出口 `PartyTownProjectionDelivery.cs:271`） | `PartyNotificationTests` 的 `Formation_is_published_in_cross_recipient_phases` 等 5 条 | 保守列入，用有序条目表达阶段屏障 |
| XO-7 | 婚礼：先向双方广播两人位置，再让任何一端开始过场 | `Protocol/Game/MarriageProposal/MarriageWeddingSlice.cs:220-222` | 无 | 保守列入，有序条目 |

保守列入的理由：XO-6、XO-7 的源码注释没有说清楚为什么要跨连接等待。但 Spec 默认要求「行为不变」，有序条目的代价只是让相关的一组连接最多被拖慢一个等待上限。如果 T-006 实施时拿到证据（协议记录或实机）表明不需要，可以带证据移出，不能默认删掉。

「请求者 Required 先发、其余 BestEffort 后发」只是 `DeliverExplicitAsync`（`Networking/Runtime/TouchSocketNetworkMessageSender.cs:113-147`）的实现顺序。没有注释或测试把它当作要求，所以不列入。
