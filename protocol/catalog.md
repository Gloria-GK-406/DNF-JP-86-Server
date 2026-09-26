# 协议清单

## 2026-09-24 提示已读状态修订

CMD `0x01DE` / NOTI `0x01A1` = Primary；CMD `0x01F8` / NOTI `0x01B9` = Secondary。
已实现两组幂等持久化及全量定向回放，分别按账号/角色归属。01F8 不再仅记录日志；01B9 不再投影成长武器。
客户端静态链及 01DE(8) 实帧已确认；本次修复待用户实机验收。详见 [调查与实现边界](../Document/outputs/notice-flags-20260924/investigation.md)。


本页记录**已在线观察到、正在做或已做完**的协议：各自处于哪一阶段、细节在哪个文档。
细节不写在这里——每行链接到该协议自己的文档。

> **找一条这里没有的协议？** 去 [Game 协议全量索引](game/index.md)。
> 那里覆盖 846 个静态 CMD 交换页（762 个可见请求 builder type 与 671 个响应 handler type 的
> 并集）和 839 个静态 NOTI 页，并显式列出 `0x0075`、`0x0084` 等实帧证明的静态盲区例外。
> 每条都有官方名与静态分析得出的 body 初稿；
> 它是**地图**，本页是**进度**。两者的关系见[清单的完整性](#清单的完整性)。
>
> **想知道这些协议怎么被串起来用的？** 那是**流程层**，不在本页也不在单条协议文档里：
> [world-entry](patterns/world-entry.md)（选角色到站进城里）、`patterns/flow-*.md`。
> 本页按 type 排列，回答"有哪些、做到哪一步"；流程层按业务顺序排列，回答"怎么用"。
> 四层分界见 [README](README.md#四层每种事实各有一个归处)。

> 2026-09-22 拾取残留修正：掉落编号改为地下城实例内唯一，跨房间不复用；
> [0x0027 编号作用域证据](game/server-to-client/NOTI-0x0027.md#掉落编号作用域2026-09-22)。2026-09-22 跨房间保留旧掉落后在新房间拾取，用户实机验收通过。

> 2026-09-24 [C2S 0027 物件身份边界](game/client-to-server/0x0027.md#物件身份边界2026-09-23)：A21 u32 代码、+22 明细数、+29+10*n 物件位已由实帧/静态确认；无已证实例标识。按 Cell/模板队列领取的单人入包已实测；组队采样显示换攻击者仍同一客户端上报，逐角色同事务发奖已通过自动化，用户确认组队背包入账并验收通过；头顶获得物品提示仍待处理。

> 2026-09-23 [CMD0204任务完成券](game/client-to-server/0x0204.md#a21-任务完成券2026-09-23)：action77的19B请求已观察；首轮实机已观察批量清理生效；因选择题漏清，范围修订为仅当前等级及以下适配角色的Epic，不按接取前置/答案分支过滤，修订待复验。

## 阶段

| 阶段 | 含义 |
|---|---|
| `未知` | 已在线观察到报文，但布局或语义尚未确立。可能只有长度、方向和出现时机。 |
| `确定语义` | 布局、字段宽度、条件分支与编码方式已有直接（指令级）证据，且至少一个真实样本可被无损解释。 |
| `实现服务端功能` | 服务端已解码为 typed message 并有消费者，parser/codec 与 handler 有回归测试。 |
| `双向验证结束` | 真实客户端端到端验证通过，双向行为（含"是否需要响应"）已用可观察结果确认。 |

进入 `双向验证结束` 需同时满足：

1. 方向、触发时机、是否需要响应均已确认（**触发时机与响应顺序记在流程层文档里**，
   不写进协议文档——协议文档只回答"这些字节是什么"）；
2. 客户端构包或解包路径有指令级证据；
3. 字段顺序、宽度、条件分支和编码方式已记录；
4. 至少一个真实样本可由记录的布局无损解释；
5. 服务端 parser/codec 与 typed message 有回归测试；
6. 实机客户端验证通过，并记录可观察结果。

## 死亡处理增量（2026-09-20，待实机）

| Command | Type | 名称 | 阶段 | 文档 |
|---|---|---|---|---|
| C2S `01` | `0028` | DIE_CHARACTER | 实现服务端功能；4/6B 解码、状态提交与幂等检查；未知字段保留 | [请求](game/client-to-server/0x0028.md) |
| S2C `00` | `0020` | DIE_STATE | 实现死亡通知；客户端静态确认读取，实机 UI 待验证 | [通知](game/server-to-client/NOTI-0x0020.md) |

## Game / Client → Server

| Command | Type | 名称 | 角色 | 阶段 | 文档 |
|---:|---:|---|---|---|---|
| `0x01` | `0x0001` | LOGIN | Query | 实现服务端功能 | [0x0001.md](game/client-to-server/0x0001.md) |
| `0x01` | `0x0002` | `SET_UDP_IP_PORT` | Mutation / 更新端点供后续队伍快照使用；当前无 CMD 回包 | ClientVerified（A21 双客户端组队路径） | [0x0002.md](game/client-to-server/0x0002.md) |
| `0x01` | `0x0003` | EXIT | Session side effect / 有响应 | 实现服务端功能（A21 七帧线级重放；真客户端完整退出待归档） | [0x0003.md](game/client-to-server/0x0003.md) |
| `0x01` | `0x0004` | `SELECT_CHARACTER` | Mutation / 有响应 | 实现服务端功能 | [0x0004.md](game/client-to-server/0x0004.md) |
| `0x01` | `0x0005` | `CREATE_CHARACTER` | Mutation / 有响应 | 实现服务端功能 | [0x0005.md](game/client-to-server/0x0005.md) |
| `0x01` | `0x0006` | `DELETE_CHARACTER` | Mutation / 有响应 | 实现服务端功能 | [0x0006.md](game/client-to-server/0x0006.md) |
| `0x01` | `0x0007` | `RETURN_SELECT_CHARACTER` | Mutation / 有响应 | 双向验证结束（A21） | [0x0007.md](game/client-to-server/0x0007.md) |
| `0x01` | `0x0008` | GET_USERINFO | Query / mode 2 名单与 mode 3 当前角色详情 | ClientVerified（A21 mode 2/3） | [0x0008.md](game/client-to-server/0x0008.md) |
| `0x01` | `0x000A` | `REQUEST_PEER` | Mutation / type 0 组队邀请；type 1/2 范围外 | ClientVerified（A21 type 0） | [0x000A.md](game/client-to-server/0x000A.md) |
| `0x01` | `0x00A6` | `CALL_PARTY_MEMBER_REALTIME_INFO` | Query / 定向刷新队员实时摘要 | ClientVerified（A21 当前在队查询） | [0x00A6.md](game/client-to-server/0x00A6.md) |
| `0x01` | `0x000B` | `RESPONSE_PEER` | Mutation / type 0 接受邀请 | ClientVerified（A21 type 0；真实 body 待归档） | [0x000B.md](game/client-to-server/0x000B.md) |
| `0x01` | `0x000C` | `SET_PARTY_INFO` | Mutation / 无队时创建、在队时更新；当前成功路径无 CMD 回包 | ClientVerified（A21 12B direct 创建/更新；`16+n` inline-title 创建；inline-title 更新/拒绝仍待实帧） | [0x000C.md](game/client-to-server/0x000C.md) |
| `0x01` | `0x000D` | `LEAVE_PARTY` | Mutation / 离队或解散 | ClientVerified（A21 普通成员、多人队长与单人队主动离队） | [0x000D.md](game/client-to-server/0x000D.md) |
| `0x01` | `0x000E` | `WALKOUT_PARTY_MEMBER` | Mutation / 队长按稳定槽位踢出成员；成功无 CMD 回包 | ClientVerified（A21 城镇两人队成功路径；拒绝与地下城路径待验证） | [0x000E.md](game/client-to-server/0x000E.md) |
| `0x01` | `0x000F` | `ENTER_SELECT_DUNGEON` | Query / 推 NOTI | 实现服务端功能 | [0x000F.md](game/client-to-server/0x000F.md) |
| `0x01` | `0x0010` | `SELECT_DUNGEON` | Query / 成功路径推 NOTI，拒绝路径有响应 | 历史 ClientVerified（教学/正式副本入场）；#101 ClientStatic 确认 u32 id/offset4 难度，现 decoder 错位，非零难度待实机 | [0x0010.md](game/client-to-server/0x0010.md) |
| `0x01` | `0x0012` | `DELETE_ITEM` | Mutation / 有响应 | ClientVerified（A21 普通装备与堆叠物） | [0x0012.md](game/client-to-server/0x0012.md) |
| `0x01` | `0x0015` | `BUY_ITEM` | Mutation / 有响应 | 实现服务端功能（A21，部分商品实机通过） | [0x0015.md](game/client-to-server/0x0015.md) |
| `0x01` | `0x0016` | `SELL_ITEM` | Mutation / 有响应 | ClientVerified（A21 普通装备）；堆叠物自动化通过，特殊装备未覆盖 | [0x0016.md](game/client-to-server/0x0016.md) |
| `0x01` | `0x0017` | `REPAIR_EQUIPMENT` | Mutation / 有响应 | 普通修理及契约自动修理已实现；自动7B严格形状、每日额度与快照已自动验证，触发/显示待实机 | [0x0017.md](game/client-to-server/0x0017.md) |
| `0x01` | `0x001A` | `DISJOINT_ITEM` | Mutation / 有响应 | ClientVerified（A21 系统/便携分解成功流程；便携 providerId=0、工具槽分档、批量逐件请求；`0x000E` 修复后当前角色刷新与同账号换角余额一致；成功 S2C body 未归档） | [0x001A.md](game/client-to-server/0x001A.md) |
| `0x01` | `0x001D` | `BUY_SKILL` | Mutation / 有响应 | 实现服务端功能（A21 普通加点，待实机） | [0x001D.md](game/client-to-server/0x001D.md) |
| `0x01` | `0x001F` | `ACCEPT_QUEST` | Mutation / 有响应 | 实现服务端功能（A21 受理请求与回包有实帧；后续任务投影未整体验证） | [0x001F.md](game/client-to-server/0x001F.md) |
| `0x01` | `0x0020` | `GIVEUP_QUEST` | Mutation / 有响应 | 实现服务端功能；A21 静态证实 `0x0020+questId` 请求及成功回包读法，实帧/UI 待验证 | [0x0020.md](game/client-to-server/0x0020.md) |
| `0x01` | `0x0021` | `SET_QUEST_TRIGGER` | Mutation / 有响应 | 实现服务端功能（A21 赛丽亚任务往返有样本；服务端事实推进与背包重算另待验证） | [0x0021.md](game/client-to-server/0x0021.md) |
| `0x01` | `0x0022` | `FINISH_QUEST` | Mutation / 有响应 | 实现服务端常规结算；A21 另一静态构包路径末字段非恒定哨兵，具体触发与实帧待查 | [0x0022.md](game/client-to-server/0x0022.md) |
| `0x01` | `0x0023` | `SET_USER_POSITION` | Report / A12 无响应，A21 推 `NOTI 0x0016` | 实现服务端功能 | [0x0023.md](game/client-to-server/0x0023.md) |
| `0x01` | `0x002D` | `MOVE_MAP` | Mutation / 成功路径推下一房间 `START_MAP` | 实现服务端功能（A21 线级重放，真客户端待复验） | [0x002D.md](game/client-to-server/0x002D.md) |
| `0x01` | `0x0030` | `DECREASE_DURABILITY` | Mutation / 有响应 | 实现服务端功能；A21 双向静态支持请求 `u8`/成功回复 `01+u8`，成对实帧和显示待验证 | [0x0030.md](game/client-to-server/0x0030.md) |
| `0x01` | `0x003F` | `CERA` | Report / 无响应 | 实现服务端功能 | [0x003F.md](game/client-to-server/0x003F.md) |
| `0x01` | `0x0040` | `BUY_CERASHOP_ITEM` | Mutation / 有响应 | 实现服务端功能（A21，宠物蛋实机成功；装扮修复待复测） | [0x0040.md](game/client-to-server/0x0040.md) |
| `0x01` | `0x0041` | `GEN_CERATICKET` | Query / 有响应 | 实现服务端功能（仅不可用反馈，待实机） | [0x0041.md](game/client-to-server/0x0041.md) |
| `0x01` | `0x0045` | `SCORE_SCROLL_STATE` | Query / 有响应 | 实现服务端功能（与牌面计时共用状态；双客户端重测待归档） | [0x0045.md](game/client-to-server/0x0045.md) |
| `0x01` | `0x0046` | `CARD_SELECT_RIGHT_STATE` | Query / 首次请求与自动计时争用同一牌面状态 | 实现服务端功能（防重复牌面；双客户端重测待归档） | [0x0046.md](game/client-to-server/0x0046.md) |
| `0x01` | `0x0047` | `SELECT_CARD` | Mutation / 有响应（认领不到牌位时不答） | 实现服务端功能 | [0x0047.md](game/client-to-server/0x0047.md) |
| `0x01` | `0x0048` | `EPLP_COMMAND` | Mutation / state 1 离开副本并恢复城镇投影 | 实现服务端功能（A21 静态布局 + 参照帧；当前真客户端待复验） | [0x0048.md](game/client-to-server/0x0048.md) |
| `0x01` | `0x0075` | `BOSS_DIE_CHECK` | Report / 无 CMD 应答 | WireCaptured（A21 单次 39B 实帧；当前只核对，不以它触发通关） | [0x0075.md](game/client-to-server/0x0075.md) |
| `0x01` | `0x0079` | `CHANGE_HOST` | Mutation / 按稳定槽位委任队长；成功无 CMD 回包 | ClientVerified（A21 城镇两人队 slot 1 接任且槽位不重排） | [0x0079.md](game/client-to-server/0x0079.md) |
| `0x01` | `0x0084` | `BACK_2_VILLAGE` | Mutation / 关闭选图界面并恢复城镇投影 | 实现服务端功能（A21 空请求实帧；实现后的按钮行为待复验） | [0x0084.md](game/client-to-server/0x0084.md) |
| `0x01` | `0x008F` | `CHANGE_TUTORIAL_FLAG` | Report / 无响应 | 双向验证结束 | [0x008F.md](game/client-to-server/0x008F.md) |
| `0x01` | `0x00A0` | `USE_BOOSTER_ITEM` | Mutation / 有响应 | 实现服务端功能（A21，待实机） | [0x00A0.md](game/client-to-server/0x00A0.md) |
| `0x01` | `0x00AB` | `CLIENT_SPEC_STATISTIC` | Report / 无响应 | 双向验证结束 | [0x00AB.md](game/client-to-server/0x00AB.md) |
| `0x01` | `0x00C2` | `FRAME_LAG_STATISTICS` | Report / 无响应 | 双向验证结束 | [0x00C2.md](game/client-to-server/0x00C2.md) |
| `0x01` | `0x00C5` | `SAVE_GAME_OPTION_1` | Report / 无响应 | 实现服务端功能 | [0x00C5.md](game/client-to-server/0x00C5.md) |
| `0x01` | `0x00D0` | `USE_RANDOMBOX_ITEM` | Mutation / 有响应 | ClientVerified（A21 赛丽亚魔盒单开；原始逐帧未归档） | [0x00D0.md](game/client-to-server/0x00D0.md) |
| `0x01` | `0x00FA` | `LODING_TIME_REPORT`（分段指标流） | Report / 无响应 | 双向验证结束 | [0x00FA.md](game/client-to-server/0x00FA.md) |
| `0x01` | `0x0117` | `LAG_STATISTICS`（多态信封） | Report / 当前 raw 消费且有意无响应 | 未知（接入不等于布局/语义闭合） | [0x0117.md](game/client-to-server/0x0117.md) |
| `0x01` | `0x0122` | `ADD_UNITED_SERVER_FRIEND` | Query / 有响应 | 已观察，未实现（本次组队范围外） | [0x0122.md](game/client-to-server/0x0122.md) |
| `0x01` | `0x01A3` | `CREATE_GROUP` | Report / 静态无响应 handler | 已观察，未实现（本次组队范围外） | [0x01A3.md](game/client-to-server/0x01A3.md) |
| `0x01` | `0x01BA` | `MERCENARY_INFO` | Query / 有响应；**另在入场批次主动发一帧** | 实现服务端功能 | [0x01BA.md](game/client-to-server/0x01BA.md) |
| `0x01` | `0x01DD` | `COMPATIBILITY_INDEX` | Report / 无响应 | 双向验证结束 | [0x01DD.md](game/client-to-server/0x01DD.md) |
| `0x01` | `0x01F8` | `INFORM_NOTICE_2ND` | 已读记录 / 无 ACK | 持久化已实现，待实机 | [0x01F8.md](game/client-to-server/0x01F8.md) |
| `0x01` | `0x0207` | `OPEN_CERAPACKAGE` | Mutation / 有响应 | 实现服务端功能（A21，待实机） | [0x0207.md](game/client-to-server/0x0207.md) |
| `0x01` | `0x0252` | `SECURITY_STATUS` | Report / 无响应 | 实现服务端功能 | [0x0252.md](game/client-to-server/0x0252.md) |
| `0x01` | `0x0286` | `CHARAC_VIEW_HIDDEN_CHARAC_INFO` | Query / 有响应 | 实现服务端功能 | [0x0286.md](game/client-to-server/0x0286.md) |
| `0x01` | `0x02A8` | `LOAD_EXTEND_CHARACS` | Report / 无响应 | 双向验证结束 | [0x02A8.md](game/client-to-server/0x02A8.md) |
| `0x01` | `0x02B3` | `STATICS_RUNTIME_TING` | Report / 无响应 | 实现服务端功能 | [0x02B3.md](game/client-to-server/0x02B3.md) |
| `0x01` | `0x02B5` | `CHECK_DOUBLE_CHARACTER_NAME` | Query / 有响应 | 实现服务端功能 | [0x02B5.md](game/client-to-server/0x02B5.md) |
| `0x01` | `0x02C1` | `CONTENTS_PLAY_INFO` | Query / 有响应 | 实现服务端功能 | [0x02C1.md](game/client-to-server/0x02C1.md) |
| `0x01` | `0x02CC` | `SHOP_PURCHASE_COUNT` | Query / 有响应 | 实现服务端功能（A21，待实机） | [0x02CC.md](game/client-to-server/0x02CC.md) |
| `0x01` | `0x0450` | `USE_RANDOMBOX_ITEM_EXPAND` | Mutation / 有响应 | ClientVerified（A21 赛丽亚魔盒批量；原始逐帧未归档） | [0x0450.md](game/client-to-server/0x0450.md) |
| `0x01` | `0x04DD` | `CHECK_USER_CONNECTION` | Query / 有响应 | 实现服务端功能 | [0x04DD.md](game/client-to-server/0x04DD.md) |

## Game / Server → Client

| Command | Type | 名称 | 角色 | 阶段 | 文档 |
|---:|---:|---|---|---|---|
| `0x01` CMD | `0x0001` | LOGIN response | Response | 实现服务端功能 | [CMD-0x0001.md](game/server-to-client/CMD-0x0001.md) |
| `0x00` NOTI | `0x0001` | `CHANNELINFO` 初始连接通知 | Notification | 实现服务端功能 | [NOTI-0x0001.md](game/server-to-client/NOTI-0x0001.md) |
| `0x00` NOTI | `0x0005` | `DUNGEON_PERMISSION` | Notification | ClientStatic（A21 5B/项；现编码3B/项，非空列表待修正验证） | [NOTI-0x0005.md](game/server-to-client/NOTI-0x0005.md) |
| `0x00` NOTI | `0x0024` | `FATIGUE` | Notification | ClientStatic（A21 5×u16；实帧/UI及业务规则未验证） | [NOTI-0x0024.md](game/server-to-client/NOTI-0x0024.md) |
| `0x01` CMD | `0x04DD` | `CHECK_USER_CONNECTION` response | Response | 实现服务端功能 | [0x04DD.md](game/client-to-server/0x04DD.md) |
| `0x00` NOTI | `0x0002` | 角色列表 | Notification | 实现服务端功能 | [NOTI-0x0002.md](game/server-to-client/NOTI-0x0002.md) |
| `0x01` CMD | `0x0003` | EXIT response | Response | 实现服务端功能（A21 七帧线级重放；真客户端完整退出待归档） | [0x0003.md](game/client-to-server/0x0003.md) |
| `0x01` CMD | `0x000E` | `WALKOUT_PARTY_MEMBER` refusal | Response / `00 13` | 实现服务端拒绝分支；上游与自动化字节向量确认，A21 实机待验证 | [0x000E.md](game/client-to-server/0x000E.md) |
| `0x01` CMD | `0x0040` | `BUY_CERASHOP_ITEM` response | Response | 实现服务端功能（A21，宠物蛋实机成功；装扮修复待复测） | [CMD-0x0040.md](game/server-to-client/CMD-0x0040.md) |
| `0x01` CMD | `0x0041` | `GEN_CERATICKET` response | Response | 实现服务端功能（失败分支，待实机） | [0x0041.md](game/client-to-server/0x0041.md) |
| `0x00` NOTI | `0x01F7` | `SERVER_NOTICE_MESSAGE` | Notification | 实现服务端功能（mode=0，商城反馈待实机） | [NOTI-0x01F7.md](game/server-to-client/NOTI-0x01F7.md) |
| `0x00` NOTI | `0x0289` | `HONOR_LEVEL_INFO` | Notification | 实现服务端功能 | [NOTI-0x0289.md](game/server-to-client/NOTI-0x0289.md) |
| `0x00` NOTI | `0x0003` | `USER_STATE` | Notification | 实现服务端功能 | [NOTI-0x0003.md](game/server-to-client/NOTI-0x0003.md) |
| `0x00` NOTI | `0x0002` | `USERINFO` | Notification | ClientVerified（A21 subtype 0 城镇同屏、subtype 1、subtype 3 角色详情） | [NOTI-0x0002.md](game/server-to-client/NOTI-0x0002.md) |
| `0x00` NOTI | `0x0006` | `USER_LEAVE` | Notification | WireCaptured（A21 双客户端正常退出的 2B 离场通知；跨区使用已被实机反证） | [NOTI-0x0006.md](game/server-to-client/NOTI-0x0006.md) |
| `0x00` NOTI | `0x0007` | `REQUEST_PEER` | Notification / 组队邀请 | ClientVerified（A21 邀请 UI；真实 body 待归档） | [NOTI-0x0007.md](game/server-to-client/NOTI-0x0007.md) |
| `0x00` NOTI | `0x0008` | `RESPONSE_PEER` | Notification / 接受回执 | Implemented（接受链 UI 通过；待逐帧归因） | [NOTI-0x0008.md](game/server-to-client/NOTI-0x0008.md) |
| `0x00` NOTI | `0x0017` | `USER_AREA` | Notification | 编码器保留，**选副本时不发**——A21 参照只在回城路上发 | [NOTI-0x0017.md](game/server-to-client/NOTI-0x0017.md) |
| `0x00` NOTI | `0x001A` | UDP/P2P host 槽位通告 | Notification | ClientVerified（A21 换队长时 slot 1；与 `0x0009 type=2` 成对验证） | [NOTI-0x001A.md](game/server-to-client/NOTI-0x001A.md) |
| `0x00` NOTI | `0x001B` | 进图选择状态 | Notification | 实现服务端功能（A21：37 字节，随 `0x0010` 的回包发） | [NOTI-0x001B.md](game/server-to-client/NOTI-0x001B.md) |
| `0x00` NOTI | `0x007C` | `ENTER_GAMEWORLD_COMPLETE` | **阶段闸点** | 双向验证结束 | [NOTI-0x007C.md](game/server-to-client/NOTI-0x007C.md) |
| `0x00` NOTI | `0x0009` | `PARTY_INFO` | Notification / baseline、refresh、settings、host-change、clear | ClientVerified（A21 type-0/type-1/type-2/clear；原生 ID `0x11` add/update/delete） | [NOTI-0x0009.md](game/server-to-client/NOTI-0x0009.md) |
| `0x00` NOTI | `0x000B` | `USER_UDP_IP_PORT` | Notification / 队员 P2P 端点 | Implemented（双端已上报建队 UI 通过；待逐帧归因/回落/P2P） | [NOTI-0x000B.md](game/server-to-client/NOTI-0x000B.md) |
| `0x00` NOTI | `0x0099` | `PARTY_MEMBER_REALTIME_INFO` | Notification / 队员实时摘要 | ClientVerified（A21 双端初始 HP） | [NOTI-0x0099.md](game/server-to-client/NOTI-0x0099.md) |
| `0x00` NOTI | `0x000D` | `ITEM_LIST` | Notification / 入场背包快照 | ClientVerified（A21 101 字节条目及虚拟材料槽；本服账号材料在同账号换角后余额一致；本次换角 S2C body 未归档） | [NOTI-0x000D.md](game/server-to-client/NOTI-0x000D.md) |
| `0x00` NOTI | `0x000E` | `UPDATE_ITEM_LIST` | Notification | ClientVerified（A21 随身金币绝对值；分解后的源槽/工具/账号晶体与品级灵魂刷新，并与换角后的 `0x000D` 重载一致；本次分解 S2C body 未归档） | [NOTI-0x000E.md](game/server-to-client/NOTI-0x000E.md) |
| `0x00` NOTI | `0x0016` | `USER_POSITION` | Notification / 兼作 `0x0023` 的应答 | 实现服务端功能 | [NOTI-0x0016.md](game/server-to-client/NOTI-0x0016.md) |
| `0x00` NOTI | `0x0018` | `AREA_USERS` | Notification | ClientVerified（A21 `[gate]` 私有、`[normal]` 共享投影；旧区权威移除待复验） | [NOTI-0x0018.md](game/server-to-client/NOTI-0x0018.md) |
| `0x00` NOTI | `0x00CA` | `VILLAGE_OBJECT_LIST` | Notification | 实现服务端功能 | [NOTI-0x00CA.md](game/server-to-client/NOTI-0x00CA.md) |
| `0x00` NOTI | `0x0165` | `CHARAC_INVISIBLE_FALGS` | Notification | 实现服务端功能 | [NOTI-0x0165.md](game/server-to-client/NOTI-0x0165.md) |
| `0x00` NOTI | `0x021E` | `SET_USABLE_COUNT_INFO` | Notification | 实现服务端功能 | [NOTI-0x021E.md](game/server-to-client/NOTI-0x021E.md) |
| `0x00` NOTI | `0x0344` | `BUY_RESTRICT_ITEM_LIST`（A21 号，A12 为 `0x02DA`） | Notification | 实现服务端功能 | [NOTI-0x0344.md](game/server-to-client/NOTI-0x0344.md) |
| `0x00` NOTI | `0x036A` | `CONTRACT_OF_CUBE_INFO`（A21 号，A12 为 `0x0300`） | Notification | 实现服务端功能 | [NOTI-0x036A.md](game/server-to-client/NOTI-0x036A.md) |
| `0x00` NOTI | `0x03C1` | `EQUIPMENT_RENTAL_LIST`（A21 号，A12 为 `0x0357`） | Notification | 实现服务端功能 | [NOTI-0x03C1.md](game/server-to-client/NOTI-0x03C1.md) |
| `0x00` NOTI | `0x0465` | `EPIC_BOOK_INFO` | Notification | 实现服务端功能 | [NOTI-0x0465.md](game/server-to-client/NOTI-0x0465.md) |
| `0x01` CMD | `0x0239` | `SET_CLONE_TITLE`（闸点之后） | Notification-shaped CMD | 实现服务端功能 | [0x0239.md](game/client-to-server/0x0239.md) |
| `0x01` CMD | `0x0009` | `RECOVER_STAMINA` | Request / response | 恢复与契约额度已有实机；成功ACK补齐u32金币余额，修复待实机（2026-09-22） | [0x0009.md](game/client-to-server/0x0009.md) |
| `0x01` CMD | `0x036F` | `PREMIUM_SERVICE`（A21 号，A12 为 `0x0312`） | Notification-shaped CMD | 购买提交后及正常入场结束发送完整快照；续期/重登期限显示ClientVerified（2026-09-22用户实测） | [CMD-0x036F.md](game/server-to-client/CMD-0x036F.md) |

角色生命周期五条 CMD 的响应与其请求同 type、同属一次交换，因此记在各自的 C→S 文档内而不另建
S→C 文档；理由见
[角色生命周期协议族](patterns/character-lifecycle.md#为什么请求和响应写在同一个文档里)。

S→C 的 command 取自服务端编码器（`GameLoginResponseEncoder` / `AccountCharacterRosterResponseEncoder`
/ `GameExitResponseEncoder`），并与客户端注册表归属交叉核对一致——**抓包对 S→C 的 command
字节零信息**，日志里的 `hdr` 是证据强度标志而非 command，详见
[game-receive](patterns/game-receive.md#抓包对-sc-的-command-字节零信息)。

服务端实现映射：CMD/NOTI 语义位于 `Dfo.Server.Protocol.Game`。每个出站 Game message
只属于 `IGameCmdMessage` 或 `IGameNotiMessage`，专属 encoder 固定写入 `0x01` 或 `0x00`，
专属 sender 方法在调用点约束类别。`Dfo.Server.Networking` 仅以不透明 `Selector + Type`
拆帧、路由和编码；全部 C→S Game decoder 由 Protocol 的 CMD helper 注册。

## A12 内容的退场

服务端服务的是 **A21**，而本资料库的绝大部分结论得出于 **A12** 客户端。两者的协议身份
本身就不一样（见上一节），所以 A12 的内容是**脚手架**，不是记录。

规则：

1. 一条协议在 A12 和 A21 上都存在、且 A12 已经做过的，**暂时保留** A12 的内容；
2. 该协议在 A21 上重做完成后，**整块删除** A12 的内容；
3. 终态：树里不再有任何 A12 内容。

为了让第 3 步可验证而不是靠记性，A12 的人工内容被包在

```html
<!-- rks:a12:begin -->  …  <!-- rks:a12:end -->
```

之间，A21 原生的页面则带 `<!-- rks:origin:a21 -->`。进度用
[`check-a12-residue.py`](patterns/data/check-a12-residue.py) 查：

```
python patterns/data/check-a12-residue.py . --list-mixed
```

它把每页归为 `a12-only` / `mixed` / `a21` / `empty` 四态。**`mixed` 就是待清单**——
那些页已经记下了 A21 的差异，但上面的字段表还是 A12 的。

**机器区已切到 A21。** 2026-08-24 已对 A21 客户端镜像重跑身份普查与布局提取，当前页脚绑定
SHA-256 `1F8244D3…`。仍带 `rks:a12` 标记的是尚未迁完的**人工区**；用
`check-a12-residue.py` 继续追踪，不能再把机器区整体描述成 A12。

## 阶段与 A21

**2026-08-24。** 上表的「阶段」列是在 **A12** 客户端上确立的。服务端现在服务 **A21**，
两者在若干协议上并不一致（`0x007C` 的 body 长度、闸点之后能不能继续发、四个号的归属……），
逐条差异记在各自文档的 `## A21` 一节，流程层差异见
[进入世界](patterns/world-entry.md)。

历史行的「双向验证结束」默认描述 A12，不能直接视为 A21 已验证。
**2026-09-08：`0x0007 RETURN_SELECT_CHARACTER` 已取得当前 A21 指令证据，并完成两轮返回选人、
再次进入的实机验证**，上表单独标记 A21；见[本次实机记录](patterns/data/traces/a21-return-selection-live-20260908.md)。
其余协议应以各自最新证据判定，不再用“所有协议都未验证”的全局结论代替逐条状态。

> 上面这句原本还写着「没有对 A21 客户端做过静态普查」。那一条已经不成立：2026-08-24 对 A21
> 镜像重跑了完整的身份普查与布局提取（[锚点](patterns/data/probes/a21-census-anchors.md)），
> 整棵文档树的机器区都由它重生成；下面「清单的完整性」数字已在本轮按当前 TSV 重算。

### 2026-08-25：入场批次逐帧对齐

本仓的入场批次与 A21 参照会话已**逐帧对齐**：两侧各 66 帧，协议多重集差为空。过程与全表见
[逐帧对账](patterns/data/traces/a21-entry-batch-diff-20260825.md)，实机会话见
[进城实录](patterns/data/traces/a21-live-entry-20260825.md)。

这一轮里有两条**被推翻的旧结论**。旧证据已从协议结论删除；下表只作为“走过的弯路”保留，
不能再被引用为当前事实：

| 旧结论 | 出处 | 推翻它的证据 |
|---|---|---|
| `GAME_OPTION` 的大小差是数据差异，不是布局差异 | [a21-world-entry-20260823](patterns/data/traces/a21-world-entry-20260823.log) `[S]` 节 | 客户端自己发的 `CMD 0x00C5` body 是 **154 = 4 + 150**，即它的选项表就是 150 字节；服务端发 134 不会解析失败，但那不是同一张表 |
| `ITEM_LIST` 的大小差是数据差异 | 同上 | 参照主背包声明 14 条、条目区 1414 字节，`1414/101` 整除——条目宽度本身变了 |

两条都属于同一类误判：**「大小不同不等于布局不同」是对的，但它不是「大小不同就一定是数据」**。
判据是能不能用角色数据把差额算平；算不平就要继续查。

## 清单的完整性

上面两张表只列**已在线观察到**的协议，不是客户端支持的全集。两个方向都已做过全量普查，
且**每一条普查到的身份现在都有自己的文档**——见 [Game 协议全量索引](game/index.md)：

| 方向 | 锚点 | 结果 | 数据 |
|---|---|---|---|
| C→S | `MakeHeader` 构包点 | 1411 个调用点；1391 点解析为 **762 type**（`0x0000`–`0x04F6`），另有 20 个未解析点 | [protocol-identity.tsv](patterns/data/protocol-identity.tsv) |
| S→C | `Register` 注册点 + 两个运行时转发器 | 1515 点 / **1510 个 `(command,type)` 对**（CMD 671 + NOTI 839） | 同上 |

> **2026-08-16**：普查已重建为单一的[身份普查](patterns/data/census-identity.py)，
> 补上了此前缺的两样东西——每条记录的**族归属**（CMD 表 / NOTI 表），以及 439 个
> **运行时转发器**注册点。旧的两份 TSV 记载"尚未写回动态注册点"的缺口至此闭合。
> 这段是 2026-08-16 的 A12 历史里程碑（604 / 538 / 728 / 两表共有 389），不是当前 A21 计数。
> 当前统计以表中的 762 / 671 / 839 和 TSV 为准。全量索引的 C→S/CMD 文档数是 846，
> 因为它还纳入 84 个只有响应 handler、静态未找到请求 builder 的 CMD type；这不是 762 的另一种写法。

**S→C 不能只看一个 type 数字。** 客户端按帧头第 0 字节
（command）把入站帧分给两张互不相干的注册表，它们对应两个**协议族**：

| 协议族 | command | 方向 | 客户端注册表 | 已普查 type | 线上 `END` 值 |
|---|---|---|---|---:|---:|
| **CMD** | `0x01` | 双向（C→S 请求 / S→C 响应） | 表 A | 671 | 1271 |
| **NOTI** | `0x00` | **仅 S→C**（服务端推送） | 表 B | 839 | 1218 |

末列是 A21 `NOTI 0x0001` 实帧携带的枚举 `END` 值，不是“已注册数量”或数组容量：CMD 为
`CmdPacketType.END = 1271`，NOTI 为 `NotiPacketType.END = 1218`。枚举成员数、可见注册身份数
与线上 END 值是三个不同口径，不能相互替代。

**客户端只发 CMD**——`MakeHeader`（所有出站帧唯一必经点）把 command 写死成 `1`。
所以 C→S 的 762 个已解析静态构包 type 全部属于 CMD 族；最大值为 `0x04F6`。像手写记录
[`0x0084`](game/client-to-server/0x0084.md) 这样的混淆区构包器仍可能落在普查盲区。

证据、分表依据与三条独立佐证见
[game-receive 的 CMD 与 NOTI](patterns/game-receive.md#两张注册表--cmd-与-noti)。

**写 S→C 的 CMD 响应前必读**：每一条 CMD 的 body 都以两字节结果信封开头
（`u8 ok`，`ok == 0` 时再加 `u8 reason`），由客户端接收层在分发之前读掉，各 type 的载荷从
第 2 或第 3 字节才开始。把状态码直接放首字节会被读成"成功"，拒绝时漏掉 reason 字节会
**吃掉下一帧的首字节**。规则与指令见
[game-receive 的两字节结果信封](patterns/game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)。
NOTI 没有信封。

方法见 [game-frame](patterns/game-frame.md) 与 [game-receive](patterns/game-receive.md)。
所有数字仍是**下限**：C→S 有被混淆到第二代码区、扫不到的构包器（已观察到的 C→S `0x0002`
就是其一）；S→C 的 handler 虽然拿不到帧缓冲，但会从全局入站流里取字段（读取原语见
[game-receive](patterns/game-receive.md#入站流与读取原语sc-的取字段方式)），
约 25% 的 handler 完全不读 body，这部分布局无法从指令推出。
上表的 671 / 839 已包含运行时注册点，且**已写回**
[protocol-identity.tsv](patterns/data/protocol-identity.tsv)。

因此本清单的作用是"哪些协议已经在做/做完"，不是"客户端有哪些协议"；后者查
[Game 协议全量索引](game/index.md)。挑下一条协议时应先在索引里查它的构包点/handler 入口
与 body 初稿，再决定是否需要抓包。

### 族文档中的早期候选

下表保留最初按族推进时的候选入口。`SELECT_CHARACTER` 现已实现，且其中部分条目已经进入上方进度表
或当前服务端接入覆盖区；“列在这里”不再表示客户端不可达或服务端未实现。当前状态必须以各协议页、
上方进度表和本页末尾的机器覆盖区交叉判断。

| Command | Type | 方向 | 名称 | 文档 |
|---:|---:|---|---|---|
| `0x01` CMD | `0x00C5` | C→S | 主游戏选项上传 | [game-settings](patterns/game-settings.md) |
| `0x01` CMD | `0x00C6` | C→S | 按键槽上传 | 同上 |
| `0x01` CMD | `0x0170` | C→S | 快速聊天上传（带 bankIndex） | 同上 |
| `0x01` CMD | `0x01C0` | C→S | 角色设置上传 | 同上 |
| `0x00` NOTI | `0x00AD` | S→C | 游戏选项催传信号（body 被丢弃） | 同上 |
| `0x00` NOTI | `0x01C7` | S→C | 按键设置下发 | 同上 |
| `0x00` NOTI | `0x0187` | S→C | 角色设置下发 | 同上 |

## 命名与身份

- 名称来源必须可区分。**2026-08-16 起本清单内已无描述性命名**：当前 A21 上游枚举表含
  `CmdPacketType` 1272 个成员与 `NotiPacketType` 1219 个成员，覆盖了本轮普查身份的命名，
  此前的三个描述性命名均已换成官方名并在各自文档保留旧名映射：

  | 旧的描述性命名 | 官方名 | 文档 |
  |---|---|---|
  | `CLIENT_SPEC_PROFILE` | `FRAME_LAG_STATISTICS` | [0x00C2.md](game/client-to-server/0x00C2.md) |
  | `CLIENT_HARDWARE_DETAIL` | `COMPATIBILITY_INDEX` | [0x01DD.md](game/client-to-server/0x01DD.md) |
  | 荣誉等级下发 | `HONOR_LEVEL_INFO` | [NOTI-0x0289.md](game/server-to-client/NOTI-0x0289.md) |

  另有四条此前记为"未命名"的协议取得官方名：`0x008F` `CHANGE_TUTORIAL_FLAG`、
  `0x00FA` `LODING_TIME_REPORT`、`0x0117` `LAG_STATISTICS`、`0x02A8` `LOAD_EXTEND_CHARACS`。
  枚举值即 wire type——该映射经本清单中 16 条已双向验证协议交叉核对，含同号异义样例，零错位。
  **名称到位不等于字段到位**：官方名说明协议整体是什么，不说明每个字段是什么。
- 同一 type 在另一方向或端点出现时，必须新增独立行和独立文档，不能覆盖已有含义。
  `0x0001`、`0x0002`、`0x0003` 都已在两个方向观察到，即为此例。
- `未知` 行没有文档链接。它们的已知观察（长度、时机、字节片段）记在下方"未知协议的现有
  观察"里，避免重复发现；一旦开始逆向，就为它建独立文档并把观察搬进去。

## 未知协议的现有观察

采样自 2026-08-14 / 2026-08-15 四次实机会话（登录 → 角色列表 → 退出），使用
`[FrameCapture]` 全量双向抓包。**方向与长度是实测的**，语义均未确认。

### 登录握手段

| Type | 方向 | Body | 观察 |
|---:|---|---:|---|
| `0x0001` | C→S | 73 | LOGIN。历史参照样本含旧账号凭证与启动参数 auth hash（样本不得入库）；当前服务端沿用两个 DString 的线格式，解释为临时 `mId` 与一次性 `ticket` |
| `0x0002` | S→C | 18 | 角色列表；样本为空名单。`0x0008` `mode=2` 的响应；服务端编码器含旧服移植的字面量尾部，未验证 |
| `0x0002` | C→S | 31 | **已定名、实现并 ClientVerified：`SET_UDP_IP_PORT`**。15 字节端点前缀为 NAT、内/外 IPv4、BE 网络序端口、LE MTU；其后身份尾部原样保留。2026-09-15 双客户端 socket 对照确认 `F5 7A → 62842`、`D8 AD → 55469`，纠正了旧 type 5 样本未经 socket 对照得出的 LE 误判；同日第二实例实测上报 MTU `512`，与客户端静态确认的 `{512, 1024, 1280, 1472}` 四档一致。当前 decoder/handler 将端点归属精确 Game Session；Party 只沿角色的当前 Session 链投影端点。客户端是否要求同 type CMD response 仍无证据，但当前无回包路径已被实际接受。构包器仍位于静态普查未覆盖的第二代码区，详见协议页。 |

### 退出 flush 段

客户端**正常退出**时按固定顺序发出（Confirmed，四次会话逐帧一致）：

```text
0x0002(31B) → 0x00C2(361B) → 0x00FA(50B) → 0x0117(12B) → 0x0003 EXIT ⇄ → 0x008F(5B)
```

四条都已建独立文档：`0x00C2` / `0x00FA` / `0x008F` 均已双向验证结束，`0x0117` 仍为未知。
`0x008F` 严格说不属于 flush——它是 EXIT **响应**的反应，详见其文档。

> **A21 的这一段不一样（2026-08-27 实测）。** 顺序是
> `0x02EF(424B) → 0x00C2(362B) → 0x045C(21B) → 0x00FA(56B) → 0x0117(61B) → 0x0003 EXIT ⇄ → 0x02B3(1B)`，
> 且 **`0x00C2` 是 362 字节而不是 361**——A21 的包头比 A12 多一字节。本仓按 361 解码抛异常、
> **连接被拆**，表现是玩家在城里按退出游戏卡死。详见
> [0x00C2](game/client-to-server/0x00C2.md#a21包头是-25-字节不是-24--而且读错会掉线)
> 与 [a21-exit-and-return-20260827](patterns/data/traces/a21-exit-and-return-20260827.md)。

| Type | 方向 | Body | 观察 |
|---:|---|---:|---|
| `0x0003` | C→S | 1 | EXIT 请求（`00`）。服务端已实现 |
| `0x0003` | S→C | 1 | EXIT 响应（`01`）。服务端已实现；目前唯一已观察到的**有响应**对 |

只进频道并停留在角色列表的会话不会出现 flush 段；**强制结束进程也不会触发 flush**。
要采样退出序列的协议，必须走游戏内正常退出。

### 抓包并不完整：seq 4 与 seq 6（**已解决**）

五次会话的发送 seq 全部形如 `0…3, 5, 7, 8, …`——**seq 4 与 seq 6 从未被抓到**。
seq 只在真正发送时递增（第五次会话少发一帧，其后每个 seq 都相应下移一位，证明了这一点），
所以这两个号确实被某次发送消耗了，只是抓包看不到。

抓包引擎挂在 `Cipher::Encrypt` 上，因此**任何不走加密路径的发送都会漏**。
影响范围：可以断言"某两帧之间没有**入站**帧"（入站有独立的 Decrypt 钩子），
但**不能**断言"客户端只发了这些帧"。各协议记录里的"无响应"结论不受影响，
"发送序列完整"这类表述则必须留有余地。

**2026-08-16 补上了缺的那一半仪器。** 服务端此前对没有 decoder 的入站帧是静默丢弃——
不记日志、不回字节；补上「未识别入站帧」日志后，这两帧当场现形：

| seq | Type | Body | 文档 |
|---:|---:|---:|---|
| 4 | `0x0286` | 0 | [0x0286.md](game/client-to-server/0x0286.md) |
| 6 | `0x01BA` | 0 | [0x01BA.md](game/client-to-server/0x01BA.md) |
| 7 | `0x0002` | 31 | 见上「登录握手段」 |

客户端侧仍只记到 `0,1,2,3,5`，两边拼起来正好连续 `0–7`。

上面关于抓包的限制**依然成立**，但现在有了互补的第二个仪器：**服务端入站日志记录一切实际到达
的字节，不管发送方走没走加密**。两者盲区不重叠，合起来才能断言"客户端确实什么都没发"。
方法上的推论见
[角色生命周期协议族](patterns/character-lifecycle.md#顺带解决的旧悬案seq-4-与-seq-6)。

### 2026-08-26：副本段

`0x000F` / `0x0010` / `0x008F` / `0x0252` / `0x0025` / `0x00BF` 及其回包在**线级**上已与 A21
参照逐帧同形，见[副本段对照](patterns/data/traces/a21-dungeon-wire-20260826.md)。
**尚未由真客户端确认**——线级重放证明服务端答了什么，不证明客户端接受什么。

同时修掉一处会让排查代价极高的形状：解码器抛出的异常此前没有报告路径，
表现为连接静默消失。详见[进入世界](patterns/world-entry.md#断线的机制)。

## 当前服务端接入覆盖（实现现状，不是协议真相）

这张表补足上方进度表没有承担的代码覆盖职责。自动检查以此区与当前源码对账；发现差异后仍需回到 A21 逆向、线级记录与真实客户端行为裁决，不能按代码反推协议。

<!-- rks:server-coverage:begin -->

> **本区由 `patterns/data/check-server-documentation-sync.py --write-catalog` 从当前
> `Dfo.Server.Protocol` 接入面生成。**它只回答“代码目前注册/可发送什么”，不证明
> type、布局、响应或客户端效果正确；成熟度仍以本页上方进度表和协议页证据为准。

### C→S：已注册 decoder

| Command | Type | 当前代码入口 | 证据边界 | 文档 |
|---:|---:|---|---|---|
| `0x01` | `0x0001` | `GameLoginMessageDecoder` | 仅证明 decoder 已注册 | [0x0001](game/client-to-server/0x0001.md) |
| `0x01` | `0x0002` | `SetUdpIpPortDecoder` | 仅证明 decoder 已注册 | [0x0002](game/client-to-server/0x0002.md) |
| `0x01` | `0x0003` | `GameExitMessageDecoder` | 仅证明 decoder 已注册 | [0x0003](game/client-to-server/0x0003.md) |
| `0x01` | `0x0004` | `SelectCharacterMessageDecoder` | 仅证明 decoder 已注册 | [0x0004](game/client-to-server/0x0004.md) |
| `0x01` | `0x0005` | `CreateCharacterMessageDecoder` | 仅证明 decoder 已注册 | [0x0005](game/client-to-server/0x0005.md) |
| `0x01` | `0x0006` | `DeleteCharacterMessageDecoder` | 仅证明 decoder 已注册 | [0x0006](game/client-to-server/0x0006.md) |
| `0x01` | `0x0007` | `ReturnToSelectionMessageDecoder` | 仅证明 decoder 已注册 | [0x0007](game/client-to-server/0x0007.md) |
| `0x01` | `0x0008` | `GetUserInfoMessageDecoder` | 仅证明 decoder 已注册 | [0x0008](game/client-to-server/0x0008.md) |
| `0x01` | `0x000A` | `RequestPeerDecoder` | 仅证明 decoder 已注册 | [0x000A](game/client-to-server/0x000A.md) |
| `0x01` | `0x000B` | `ResponsePeerDecoder` | 仅证明 decoder 已注册 | [0x000B](game/client-to-server/0x000B.md) |
| `0x01` | `0x000C` | `SetPartyInfoDecoder` | 仅证明 decoder 已注册 | [0x000C](game/client-to-server/0x000C.md) |
| `0x01` | `0x000D` | `LeavePartyDecoder` | 仅证明 decoder 已注册 | [0x000D](game/client-to-server/0x000D.md) |
| `0x01` | `0x000F` | `EnterSelectDungeonDecoder` | 仅证明 decoder 已注册 | [0x000F](game/client-to-server/0x000F.md) |
| `0x01` | `0x0010` | `SelectDungeonDecoder` | 仅证明 decoder 已注册 | [0x0010](game/client-to-server/0x0010.md) |
| `0x01` | `0x0012` | `DeleteItemMessageDecoder` | 仅证明 decoder 已注册 | [0x0012](game/client-to-server/0x0012.md) |
| `0x01` | `0x0013` | `MoveItemSpaceDecoder` | 仅证明 decoder 已注册 | [0x0013](game/client-to-server/0x0013.md) |
| `0x01` | `0x0015` | `BuyItemMessageDecoder` | 仅证明 decoder 已注册 | [0x0015](game/client-to-server/0x0015.md) |
| `0x01` | `0x0016` | `SellItemMessageDecoder` | 仅证明 decoder 已注册 | [0x0016](game/client-to-server/0x0016.md) |
| `0x01` | `0x0017` | `RepairItemMessageDecoder` | 仅证明 decoder 已注册 | [0x0017](game/client-to-server/0x0017.md) |
| `0x01` | `0x001D` | `LearnSkillMessageDecoder` | 仅证明 decoder 已注册 | [0x001D](game/client-to-server/0x001D.md) |
| `0x01` | `0x001F` | `AcceptQuestMessageDecoder` | 仅证明 decoder 已注册 | [0x001F](game/client-to-server/0x001F.md) |
| `0x01` | `0x0020` | `GiveUpQuestMessageDecoder` | 仅证明 decoder 已注册 | [0x0020](game/client-to-server/0x0020.md) |
| `0x01` | `0x0021` | `SetQuestTriggerMessageDecoder` | 仅证明 decoder 已注册 | [0x0021](game/client-to-server/0x0021.md) |
| `0x01` | `0x0022` | `FinishQuestMessageDecoder` | 仅证明 decoder 已注册 | [0x0022](game/client-to-server/0x0022.md) |
| `0x01` | `0x0023` | `SetUserPositionDecoder` | 仅证明 decoder 已注册 | [0x0023](game/client-to-server/0x0023.md) |
| `0x01` | `0x0024` | `SetUserAreaDecoder` | 仅证明 decoder 已注册 | [0x0024](game/client-to-server/0x0024.md) |
| `0x01` | `0x0025` | `FinishLoadingDecoder` | 仅证明 decoder 已注册 | [0x0025](game/client-to-server/0x0025.md) |
| `0x01` | `0x0026` | `UseSkillDecoder` | 仅证明 decoder 已注册 | [0x0026](game/client-to-server/0x0026.md) |
| `0x01` | `0x0027` | `MonsterDeathDecoder` | 仅证明 decoder 已注册 | [0x0027](game/client-to-server/0x0027.md) |
| `0x01` | `0x002A` | `GiveUpGameDecoder` | 仅证明 decoder 已注册 | [0x002A](game/client-to-server/0x002A.md) |
| `0x01` | `0x002B` | `GetItemDecoder` | 仅证明 decoder 已注册 | [0x002B](game/client-to-server/0x002B.md) |
| `0x01` | `0x002C` | `UseStackableDecoder` | 仅证明 decoder 已注册 | [0x002C](game/client-to-server/0x002C.md) |
| `0x01` | `0x002D` | `MoveMapDecoder` | 仅证明 decoder 已注册 | [0x002D](game/client-to-server/0x002D.md) |
| `0x01` | `0x002E` | `SetPlayResultDecoder` | 仅证明 decoder 已注册 | [0x002E](game/client-to-server/0x002E.md) |
| `0x01` | `0x0030` | `DecreaseDurabilityMessageDecoder` | 仅证明 decoder 已注册 | [0x0030](game/client-to-server/0x0030.md) |
| `0x01` | `0x003F` | `CeraQueryDecoder` | 仅证明 decoder 已注册 | [0x003F](game/client-to-server/0x003F.md) |
| `0x01` | `0x0040` | `CeraShopPurchaseMessageDecoder` | 仅证明 decoder 已注册 | [0x0040](game/client-to-server/0x0040.md) |
| `0x01` | `0x0041` | `GenCeraTicketMessageDecoder` | 仅证明 decoder 已注册 | [0x0041](game/client-to-server/0x0041.md) |
| `0x01` | `0x0045` | `ScoreScrollStateDecoder` | 仅证明 decoder 已注册 | [0x0045](game/client-to-server/0x0045.md) |
| `0x01` | `0x0046` | `CardSelectRightStateDecoder` | 仅证明 decoder 已注册 | [0x0046](game/client-to-server/0x0046.md) |
| `0x01` | `0x0047` | `SelectCardDecoder` | 仅证明 decoder 已注册 | [0x0047](game/client-to-server/0x0047.md) |
| `0x01` | `0x0048` | `EplpCommandDecoder` | 仅证明 decoder 已注册 | [0x0048](game/client-to-server/0x0048.md) |
| `0x01` | `0x0075` | `BossDieCheckDecoder` | 仅证明 decoder 已注册 | [0x0075](game/client-to-server/0x0075.md) |
| `0x01` | `0x0078` | `RequestBlacklistDecoder` | 仅证明 decoder 已注册 | [0x0078](game/client-to-server/0x0078.md) |
| `0x01` | `0x007B` | `CharacterStatisticDecoder` | 仅证明 decoder 已注册 | [0x007B](game/client-to-server/0x007B.md) |
| `0x01` | `0x0084` | `BackToVillageDecoder` | 仅证明 decoder 已注册 | [0x0084](game/client-to-server/0x0084.md) |
| `0x01` | `0x008F` | `TaggedIdReportMessageDecoder` | 仅证明 decoder 已注册 | [0x008F](game/client-to-server/0x008F.md) |
| `0x01` | `0x00A0` | `PackageOpeningDecoder` | 仅证明 decoder 已注册 | [0x00A0](game/client-to-server/0x00A0.md) |
| `0x01` | `0x00A6` | `CallPartyMemberRealtimeInfoDecoder` | 仅证明 decoder 已注册 | [0x00A6](game/client-to-server/0x00A6.md) |
| `0x01` | `0x00AB` | `ClientSpecStatisticMessageDecoder` | 仅证明 decoder 已注册 | [0x00AB](game/client-to-server/0x00AB.md) |
| `0x01` | `0x00BF` | `StoryPauseDecoder` | 仅证明 decoder 已注册 | [0x00BF](game/client-to-server/0x00BF.md) |
| `0x01` | `0x00C2` | `ClientSpecProfileMessageDecoder` | 仅证明 decoder 已注册 | [0x00C2](game/client-to-server/0x00C2.md) |
| `0x01` | `0x00C5` | `SaveGameOption1MessageDecoder` | 仅证明 decoder 已注册 | [0x00C5](game/client-to-server/0x00C5.md) |
| `0x01` | `0x00D0` | `MagicBoxSingleOpeningDecoder` | 仅证明 decoder 已注册 | [0x00D0](game/client-to-server/0x00D0.md) |
| `0x01` | `0x00D9` | `OverflowInfoDecoder` | 仅证明 decoder 已注册 | [0x00D9](game/client-to-server/0x00D9.md) |
| `0x01` | `0x00FA` | `SessionMetricsMessageDecoder` | 仅证明 decoder 已注册 | [0x00FA](game/client-to-server/0x00FA.md) |
| `0x01` | `0x0117` | `LagStatisticsDecoder` | 仅证明 decoder 已注册 | [0x0117](game/client-to-server/0x0117.md) |
| `0x01` | `0x011B` | `PiDecoder` | 仅证明 decoder 已注册 | [0x011B](game/client-to-server/0x011B.md) |
| `0x01` | `0x01BA` | `MercenaryInfoMessageDecoder` | 仅证明 decoder 已注册 | [0x01BA](game/client-to-server/0x01BA.md) |
| `0x01` | `0x01DD` | `ClientHardwareDetailMessageDecoder` | 仅证明 decoder 已注册 | [0x01DD](game/client-to-server/0x01DD.md) |
| `0x01` | `0x01E4` | `TutorialLevelUpDecoder` | 仅证明 decoder 已注册 | [0x01E4](game/client-to-server/0x01E4.md) |
| `0x01` | `0x01DE` | `NoticeSeenDecoder` | 提示记录上报已实现，待实机 | [0x01DE](game/client-to-server/0x01DE.md) |
| `0x01` | `0x01F8` | `InformNoticeSecondDecoder` | 仅证明 decoder 已注册 | [0x01F8](game/client-to-server/0x01F8.md) |
| `0x01` | `0x0207` | `AvatarPackageOpeningDecoder` | 仅证明 decoder 已注册 | [0x0207](game/client-to-server/0x0207.md) |
| `0x01` | `0x0252` | `SecurityStatusDecoder` | 仅证明 decoder 已注册 | [0x0252](game/client-to-server/0x0252.md) |
| `0x01` | `0x0286` | `HiddenCharacterInfoMessageDecoder` | 仅证明 decoder 已注册 | [0x0286](game/client-to-server/0x0286.md) |
| `0x01` | `0x02A8` | `RosterFollowUpMessageDecoder` | 仅证明 decoder 已注册 | [0x02A8](game/client-to-server/0x02A8.md) |
| `0x01` | `0x02B3` | `RuntimeTingStatisticDecoder` | 仅证明 decoder 已注册 | [0x02B3](game/client-to-server/0x02B3.md) |
| `0x01` | `0x02B5` | `CheckCharacterNameMessageDecoder` | 仅证明 decoder 已注册 | [0x02B5](game/client-to-server/0x02B5.md) |
| `0x01` | `0x02BE` | `FarmEventActionDecoder` | 仅证明 decoder 已注册 | [0x02BE](game/client-to-server/0x02BE.md) |
| `0x01` | `0x02C1` | `ContentsPlayInfoMessageDecoder` | 仅证明 decoder 已注册 | [0x02C1](game/client-to-server/0x02C1.md) |
| `0x01` | `0x02CC` | `ShopPurchaseCountDecoder` | 仅证明 decoder 已注册 | [0x02CC](game/client-to-server/0x02CC.md) |
| `0x01` | `0x02EF` | `FpsPerformanceStatisticDecoder` | 仅证明 decoder 已注册 | [0x02EF](game/client-to-server/0x02EF.md) |
| `0x01` | `0x035C` | `SyncItemSpaceDecoder` | 仅证明 decoder 已注册 | [0x035C](game/client-to-server/0x035C.md) |
| `0x01` | `0x039D` | `RequestIgaVersionDecoder` | 仅证明 decoder 已注册 | [0x039D](game/client-to-server/0x039D.md) |
| `0x01` | `0x0450` | `MagicBoxBatchOpeningDecoder` | 仅证明 decoder 已注册 | [0x0450](game/client-to-server/0x0450.md) |
| `0x01` | `0x045C` | `FrameLagPerDungeonDecoder` | 仅证明 decoder 已注册 | [0x045C](game/client-to-server/0x045C.md) |
| `0x01` | `0x04D7` | `LogConnectProcessDecoder` | 仅证明 decoder 已注册 | [0x04D7](game/client-to-server/0x04D7.md) |
| `0x01` | `0x04DD` | `ConnectionCheckMessageDecoder` | 仅证明 decoder 已注册 | [0x04DD](game/client-to-server/0x04DD.md) |
| `0x01` | `0x04F1` | `LogDomainConnectDecoder` | 仅证明 decoder 已注册 | [0x04F1](game/client-to-server/0x04F1.md) |

### S→C：encoder-backed wire identity

| Command | Type | 当前代码入口 | 证据边界 | 文档 |
|---:|---:|---|---|---|
| `0x01` CMD | `0x0001` | `GameLoginResponse` | 仅证明 encoder 可产生该 identity | [0x0001](game/client-to-server/0x0001.md) |
| `0x01` CMD | `0x0003` | `GameExitResponse` | 仅证明 encoder 可产生该 identity | [0x0003](game/client-to-server/0x0003.md) |
| `0x01` CMD | `0x0004` | `CharacterEntryAcceptedMessage`、`EntryAdmissionRejectedCommandMessage` | 仅证明 encoder 可产生该 identity | [0x0004](game/client-to-server/0x0004.md) |
| `0x01` CMD | `0x0005` | `CreateCharacterResponse` | 仅证明 encoder 可产生该 identity | [0x0005](game/client-to-server/0x0005.md) |
| `0x01` CMD | `0x0006` | `DeleteCharacterResponse` | 仅证明 encoder 可产生该 identity | [0x0006](game/client-to-server/0x0006.md) |
| `0x01` CMD | `0x0007` | `ReturnToSelectionResponse` | 仅证明 encoder 可产生该 identity | [0x0007](game/client-to-server/0x0007.md) |
| `0x01` CMD | `0x000F` | `EnterSelectDungeonAcknowledgement` | 仅证明 encoder 可产生该 identity | [0x000F](game/client-to-server/0x000F.md) |
| `0x01` CMD | `0x0010` | `SelectDungeonRefusal` | 仅证明 encoder 可产生该 identity | [0x0010](game/client-to-server/0x0010.md) |
| `0x01` CMD | `0x0012` | `DeleteItemResponse` | 仅证明 encoder 可产生该 identity | [0x0012](game/client-to-server/0x0012.md) |
| `0x01` CMD | `0x0013` | `MoveItemSpaceResponse` | 仅证明 encoder 可产生该 identity | [0x0013](game/client-to-server/0x0013.md) |
| `0x01` CMD | `0x0015` | `BuyItemResponse` | 仅证明 encoder 可产生该 identity | [0x0015](game/client-to-server/0x0015.md) |
| `0x01` CMD | `0x0016` | `SellItemResponse` | 仅证明 encoder 可产生该 identity | [0x0016](game/client-to-server/0x0016.md) |
| `0x01` CMD | `0x0017` | `RepairItemResponse` | 仅证明 encoder 可产生该 identity | [0x0017](game/client-to-server/0x0017.md) |
| `0x01` CMD | `0x001A` | `DisjointItemResponse` | 仅证明 encoder 可产生该 identity | [0x001A](game/client-to-server/0x001A.md) |
| `0x01` CMD | `0x001D` | `LearnSkillResponse` | 仅证明 encoder 可产生该 identity | [0x001D](game/client-to-server/0x001D.md) |
| `0x01` CMD | `0x001F` | `AcceptQuestAcknowledgement` | 仅证明 encoder 可产生该 identity | [0x001F](game/client-to-server/0x001F.md) |
| `0x01` CMD | `0x0020` | `GiveUpQuestAcknowledgement` | 仅证明 encoder 可产生该 identity | [0x0020](game/client-to-server/0x0020.md) |
| `0x01` CMD | `0x0021` | `SetQuestTriggerAcknowledgement` | 仅证明 encoder 可产生该 identity | [0x0021](game/client-to-server/0x0021.md) |
| `0x01` CMD | `0x0022` | `FinishQuestAcknowledgement` | 仅证明 encoder 可产生该 identity | [0x0022](game/client-to-server/0x0022.md) |
| `0x01` CMD | `0x0025` | `FinishLoadingResponse` | 仅证明 encoder 可产生该 identity | [0x0025](game/client-to-server/0x0025.md) |
| `0x01` CMD | `0x002A` | `GiveUpGameResponse` | 仅证明 encoder 可产生该 identity | [0x002A](game/client-to-server/0x002A.md) |
| `0x01` CMD | `0x002B` | `GetItemResponse` | 仅证明 encoder 可产生该 identity | [0x002B](game/client-to-server/0x002B.md) |
| `0x01` CMD | `0x002C` | `UseStackableResponse` | 仅证明 encoder 可产生该 identity | [0x002C](game/client-to-server/0x002C.md) |
| `0x01` CMD | `0x0030` | `DecreaseDurabilityResponse` | 仅证明 encoder 可产生该 identity | [0x0030](game/client-to-server/0x0030.md) |
| `0x01` CMD | `0x0040` | `CeraShopPurchaseResponse` | 仅证明 encoder 可产生该 identity | [0x0040](game/client-to-server/0x0040.md) |
| `0x01` CMD | `0x0041` | `GenCeraTicketUnavailable` | 仅证明 encoder 可产生该 identity | [0x0041](game/client-to-server/0x0041.md) |
| `0x01` CMD | `0x0045` | `ScoreScrollStateResponse` | 仅证明 encoder 可产生该 identity | [0x0045](game/client-to-server/0x0045.md) |
| `0x01` CMD | `0x0046` | `CardSelectRightStateResponse` | 仅证明 encoder 可产生该 identity | [0x0046](game/client-to-server/0x0046.md) |
| `0x01` CMD | `0x0047` | `SelectCardResponse` | 仅证明 encoder 可产生该 identity | [0x0047](game/client-to-server/0x0047.md) |
| `0x01` CMD | `0x0048` | `EplpCommandResponse` | 仅证明 encoder 可产生该 identity | [0x0048](game/client-to-server/0x0048.md) |
| `0x01` CMD | `0x0084` | `BackToVillageResponse` | 仅证明 encoder 可产生该 identity | [0x0084](game/client-to-server/0x0084.md) |
| `0x01` CMD | `0x008F` | `TutorialFlagAcknowledgement` | 仅证明 encoder 可产生该 identity | [0x008F](game/client-to-server/0x008F.md) |
| `0x01` CMD | `0x00A0` | `OpeningRewardPopup`、`PackageOpeningResponse` | 仅证明 encoder 可产生该 identity | [0x00A0](game/client-to-server/0x00A0.md) |
| `0x01` CMD | `0x00D0` | `MagicBoxSingleOpeningResponse` | 仅证明 encoder 可产生该 identity | [0x00D0](game/client-to-server/0x00D0.md) |
| `0x01` CMD | `0x01BA` | `MercenaryInfoResponse`、`MercenarySupportStateMessage` | 仅证明 encoder 可产生该 identity | [0x01BA](game/client-to-server/0x01BA.md) |
| `0x01` CMD | `0x01E4` | `TutorialLevelUpResponse` | 仅证明 encoder 可产生该 identity | [0x01E4](game/client-to-server/0x01E4.md) |
| `0x01` CMD | `0x0207` | `AvatarPackageOpeningResponse` | 仅证明 encoder 可产生该 identity | [0x0207](game/client-to-server/0x0207.md) |
| `0x01` CMD | `0x0239` | `CloneTitleStateMessage` | 仅证明 encoder 可产生该 identity | [0x0239](game/client-to-server/0x0239.md) |
| `0x01` CMD | `0x0252` | `SecurityStatusAcknowledgement` | 仅证明 encoder 可产生该 identity | [0x0252](game/client-to-server/0x0252.md) |
| `0x01` CMD | `0x0286` | `HiddenCharacterInfoResponse` | 仅证明 encoder 可产生该 identity | [0x0286](game/client-to-server/0x0286.md) |
| `0x01` CMD | `0x02A8` | `RosterFollowUpResponse` | 仅证明 encoder 可产生该 identity | [0x02A8](game/client-to-server/0x02A8.md) |
| `0x01` CMD | `0x02B5` | `CheckCharacterNameResponse` | 仅证明 encoder 可产生该 identity | [0x02B5](game/client-to-server/0x02B5.md) |
| `0x01` CMD | `0x02C1` | `ContentsPlayInfoResponse` | 仅证明 encoder 可产生该 identity | [0x02C1](game/client-to-server/0x02C1.md) |
| `0x01` CMD | `0x02CC` | `ShopPurchaseCountResponse` | 仅证明 encoder 可产生该 identity | [0x02CC](game/client-to-server/0x02CC.md) |
| `0x01` CMD | `0x036F` | `PremiumServiceStateMessage` | 续期及重登期限显示、槽0黄金翻牌及槽3BUFF次数边界ClientVerified；其他范围见记录 | [CMD-0x036F](game/server-to-client/CMD-0x036F.md) |
| `0x01` CMD | `0x0450` | `MagicBoxBatchOpeningResponse` | 仅证明 encoder 可产生该 identity | [0x0450](game/client-to-server/0x0450.md) |
| `0x01` CMD | `0x04DD` | `ConnectionCheckResponse` | 仅证明 encoder 可产生该 identity | [0x04DD](game/client-to-server/0x04DD.md) |
| `0x00` NOTI | `0x0001` | `GameInitialConnectionNotice` | 仅证明 encoder 可产生该 identity | [0x0001](game/server-to-client/NOTI-0x0001.md) |
| `0x00` NOTI | `0x0002` | `AccountCharacterRosterResponse`、`AccountRosterStateMessage`、`CurrentUserStateMessage`、`ExtendedUserStateMessage`、`OtherCharacterDetailResponse`、`PvpLobbySnapshotMessage`、`PvpParticipantPublicationMessage`、`TownReadyStateMessage` | 仅证明 encoder 可产生该 identity | [0x0002](game/server-to-client/NOTI-0x0002.md) |
| `0x00` NOTI | `0x0003` | `UserStateBaselineMessage`、`UserStateNotification` | 仅证明 encoder 可产生该 identity | [0x0003](game/server-to-client/NOTI-0x0003.md) |
| `0x00` NOTI | `0x0005` | `DungeonPermissionStateMessage` | 仅证明 encoder 可产生该 identity | [0x0005](game/server-to-client/NOTI-0x0005.md) |
| `0x00` NOTI | `0x0006` | `TownDepartureMessage` | 仅证明 encoder 可产生该 identity | [0x0006](game/server-to-client/NOTI-0x0006.md) |
| `0x00` NOTI | `0x0007` | `PartyInviteRequestMessage` | 仅证明 encoder 可产生该 identity | [0x0007](game/server-to-client/NOTI-0x0007.md) |
| `0x00` NOTI | `0x0008` | `PartyInviteAcceptedMessage` | 仅证明 encoder 可产生该 identity | [0x0008](game/server-to-client/NOTI-0x0008.md) |
| `0x00` NOTI | `0x0009` | `PartyBaselineMessage`、`PartyRosterMessage`、`PartySettingsMessage` | 仅证明 encoder 可产生该 identity | [0x0009](game/server-to-client/NOTI-0x0009.md) |
| `0x00` NOTI | `0x000B` | `PartyEndpointsMessage` | 仅证明 encoder 可产生该 identity | [0x000B](game/server-to-client/NOTI-0x000B.md) |
| `0x00` NOTI | `0x000D` | `GuildMedalBaselineMessage`、`InventorySnapshotMessage` | 仅证明 encoder 可产生该 identity | [0x000D](game/server-to-client/NOTI-0x000D.md) |
| `0x00` NOTI | `0x000E` | `InventoryDeltaMessage` | 仅证明 encoder 可产生该 identity | [0x000E](game/server-to-client/NOTI-0x000E.md) |
| `0x00` NOTI | `0x0013` | `SkillStateMessage` | 仅证明 encoder 可产生该 identity | [0x0013](game/server-to-client/NOTI-0x0013.md) |
| `0x00` NOTI | `0x0015` | `AvailableQuestListMessage` | 仅证明 encoder 可产生该 identity | [0x0015](game/server-to-client/NOTI-0x0015.md) |
| `0x00` NOTI | `0x0016` | `CharacterPositionStateMessage`、`UserPositionNotification` | 仅证明 encoder 可产生该 identity | [0x0016](game/server-to-client/NOTI-0x0016.md) |
| `0x00` NOTI | `0x0017` | `UserAreaNotification` | 仅证明 encoder 可产生该 identity | [0x0017](game/server-to-client/NOTI-0x0017.md) |
| `0x00` NOTI | `0x0018` | `AreaUsersNotification` | 仅证明 encoder 可产生该 identity | [0x0018](game/server-to-client/NOTI-0x0018.md) |
| `0x00` NOTI | `0x001A` | `UdpHostNotification` | 仅证明 encoder 可产生该 identity | [0x001A](game/server-to-client/NOTI-0x001A.md) |
| `0x00` NOTI | `0x001B` | `EnterSelectDungeonNotification` | 仅证明 encoder 可产生该 identity | [0x001B](game/server-to-client/NOTI-0x001B.md) |
| `0x00` NOTI | `0x001C` | `DungeonInfoNotification` | 仅证明 encoder 可产生该 identity | [0x001C](game/server-to-client/NOTI-0x001C.md) |
| `0x00` NOTI | `0x001D` | `StartMapNotification` | 仅证明 encoder 可产生该 identity | [0x001D](game/server-to-client/NOTI-0x001D.md) |
| `0x00` NOTI | `0x001E` | `LoadingCompleteNotification` | 仅证明 encoder 可产生该 identity | [0x001E](game/server-to-client/NOTI-0x001E.md) |
| `0x00` NOTI | `0x001F` | `EnableClearDungeonNotification` | 仅证明 encoder 可产生该 identity | [0x001F](game/server-to-client/NOTI-0x001F.md) |
| `0x00` NOTI | `0x0022` | `PlayResultNotification` | 仅证明 encoder 可产生该 identity | [0x0022](game/server-to-client/NOTI-0x0022.md) |
| `0x00` NOTI | `0x0023` | `ClearDungeonRewardNotification` | 仅证明 encoder 可产生该 identity | [0x0023](game/server-to-client/NOTI-0x0023.md) |
| `0x00` NOTI | `0x0025` | `ExperienceNotification`、`GrowthCapsuleStateMessage` | 仅证明 encoder 可产生该 identity | [0x0025](game/server-to-client/NOTI-0x0025.md) |
| `0x00` NOTI | `0x0026` | `MonsterDeathNotification` | 仅证明 encoder 可产生该 identity | [0x0026](game/server-to-client/NOTI-0x0026.md) |
| `0x00` NOTI | `0x0027` | `GroundGoldPickupNotification`、`GroundPickupNotification` | 仅证明 encoder 可产生该 identity | [0x0027](game/server-to-client/NOTI-0x0027.md) |
| `0x00` NOTI | `0x0029` | `PvpLobbySnapshotMessage` | 仅证明 encoder 可产生该 identity | [0x0029](game/server-to-client/NOTI-0x0029.md) |
| `0x00` NOTI | `0x0035` | `CeraBalanceStateMessage`、`CeraBalanceUpdateMessage` | 仅证明 encoder 可产生该 identity | [0x0035](game/server-to-client/NOTI-0x0035.md) |
| `0x00` NOTI | `0x0042` | `PremiumGrantedMessage` | 仅证明 encoder 可产生该 identity | [0x0042](game/server-to-client/NOTI-0x0042.md) |
| `0x00` NOTI | `0x0061` | `MailboxStateMessage` | 仅证明 encoder 可产生该 identity | [0x0061](game/server-to-client/NOTI-0x0061.md) |
| `0x00` NOTI | `0x0067` | `PetRevivalStateMessage` | 仅证明 encoder 可产生该 identity | [0x0067](game/server-to-client/NOTI-0x0067.md) |
| `0x00` NOTI | `0x0069` | `CreatureListMessage`、`CreatureListUpdateMessage` | 仅证明 encoder 可产生该 identity | [0x0069](game/server-to-client/NOTI-0x0069.md) |
| `0x00` NOTI | `0x006B` | `PetRevivalStateMessage` | 仅证明 encoder 可产生该 identity | [0x006B](game/server-to-client/NOTI-0x006B.md) |
| `0x00` NOTI | `0x006C` | `ServerEventBaselineMessage` | 仅证明 encoder 可产生该 identity | [0x006C](game/server-to-client/NOTI-0x006C.md) |
| `0x00` NOTI | `0x0077` | `PetWelcomeStateMessage` | 仅证明 encoder 可产生该 identity | [0x0077](game/server-to-client/NOTI-0x0077.md) |
| `0x00` NOTI | `0x007C` | `EntryMarkerMessage` | 仅证明 encoder 可产生该 identity | [0x007C](game/server-to-client/NOTI-0x007C.md) |
| `0x00` NOTI | `0x0099` | `PartyRealtimeMessage` | 仅证明 encoder 可产生该 identity | [0x0099](game/server-to-client/NOTI-0x0099.md) |
| `0x00` NOTI | `0x00AA` | `StoryPauseNotification` | 仅证明 encoder 可产生该 identity | [0x00AA](game/server-to-client/NOTI-0x00AA.md) |
| `0x00` NOTI | `0x00AC` | `ItemCooldownStateMessage` | 仅证明 encoder 可产生该 identity | [0x00AC](game/server-to-client/NOTI-0x00AC.md) |
| `0x00` NOTI | `0x00AD` | `AccountGameOptionsMessage` | 仅证明 encoder 可产生该 identity | [0x00AD](game/server-to-client/NOTI-0x00AD.md) |
| `0x00` NOTI | `0x00AE` | `ItemEffectStateMessage` | 仅证明 encoder 可产生该 identity | [0x00AE](game/server-to-client/NOTI-0x00AE.md) |
| `0x00` NOTI | `0x00B1` | `DailyReviveCoinStateMessage` | 仅证明 encoder 可产生该 identity | [0x00B1](game/server-to-client/NOTI-0x00B1.md) |
| `0x00` NOTI | `0x00B7` | `AuctionServiceStateNotice` | 仅证明 encoder 可产生该 identity | [0x00B7](game/server-to-client/NOTI-0x00B7.md) |
| `0x00` NOTI | `0x00CA` | `VillageObjectListMessage` | 仅证明 encoder 可产生该 identity | [0x00CA](game/server-to-client/NOTI-0x00CA.md) |
| `0x00` NOTI | `0x00CD` | `ExpertJobStateMessage` | 仅证明 encoder 可产生该 identity | [0x00CD](game/server-to-client/NOTI-0x00CD.md) |
| `0x00` NOTI | `0x00FB` | `EquipmentLockStateMessage`、`TrailingEquipmentLockSnapshotMessage` | 仅证明 encoder 可产生该 identity | [0x00FB](game/server-to-client/NOTI-0x00FB.md) |
| `0x00` NOTI | `0x0111` | `CrossServerFriendBaselineMessage` | 仅证明 encoder 可产生该 identity | [0x0111](game/server-to-client/NOTI-0x0111.md) |
| `0x00` NOTI | `0x0117` | `SecretShopNpcNotification` | 仅证明 encoder 可产生该 identity | [0x0117](game/server-to-client/NOTI-0x0117.md) |
| `0x00` NOTI | `0x0158` | `PvpMissionStateMessage` | 仅证明 encoder 可产生该 identity | [0x0158](game/server-to-client/NOTI-0x0158.md) |
| `0x00` NOTI | `0x015F` | `SkillPointSlotStateMessage` | 仅证明 encoder 可产生该 identity | [0x015F](game/server-to-client/NOTI-0x015F.md) |
| `0x00` NOTI | `0x0164` | `QuestVisibilityMessage` | 仅证明 encoder 可产生该 identity | [0x0164](game/server-to-client/NOTI-0x0164.md) |
| `0x00` NOTI | `0x0165` | `UserVisibilityMessage` | 仅证明 encoder 可产生该 identity | [0x0165](game/server-to-client/NOTI-0x0165.md) |
| `0x00` NOTI | `0x0166` | `TitleBookStateMessage` | 仅证明 encoder 可产生该 identity | [0x0166](game/server-to-client/NOTI-0x0166.md) |
| `0x00` NOTI | `0x0167` | `AchievementStateMessage` | 仅证明 encoder 可产生该 identity | [0x0167](game/server-to-client/NOTI-0x0167.md) |
| `0x00` NOTI | `0x017B` | `ClientEffectBaselineMessage` | 仅证明 encoder 可产生该 identity | [0x017B](game/server-to-client/NOTI-0x017B.md) |
| `0x00` NOTI | `0x0187` | `CharacterOptionStateMessage` | 仅证明 encoder 可产生该 identity | [0x0187](game/server-to-client/NOTI-0x0187.md) |
| `0x00` NOTI | `0x019D` | `LuckyStarStateMessage` | 仅证明 encoder 可产生该 identity | [0x019D](game/server-to-client/NOTI-0x019D.md) |
| `0x00` NOTI | `0x019F` | `SupportCharacterStateMessage`、`SupportTagNotification` | 仅证明 encoder 可产生该 identity | [0x019F](game/server-to-client/NOTI-0x019F.md) |
| `0x00` NOTI | `0x01A1` | `InformNoticeNotice` | 仅证明 encoder 可产生该 identity | [0x01A1](game/server-to-client/NOTI-0x01A1.md) |
| `0x00` NOTI | `0x01A8` | `PcRoomPlayTimeStateMessage` | 仅证明 encoder 可产生该 identity | [0x01A8](game/server-to-client/NOTI-0x01A8.md) |
| `0x00` NOTI | `0x01B9` | `SecondaryNoticeStateMessage` | 仅证明 encoder 可产生该 identity | [0x01B9](game/server-to-client/NOTI-0x01B9.md) |
| `0x00` NOTI | `0x01C0` | `DarkKnightComboStateMessage` | 仅证明 encoder 可产生该 identity | [0x01C0](game/server-to-client/NOTI-0x01C0.md) |
| `0x00` NOTI | `0x01C7` | `HotkeyStateMessage`、`LoginHotkeyStateMessage` | 仅证明 encoder 可产生该 identity | [0x01C7](game/server-to-client/NOTI-0x01C7.md) |
| `0x00` NOTI | `0x01EB` | `FatigueAccelerationStateMessage` | 仅证明 encoder 可产生该 identity | [0x01EB](game/server-to-client/NOTI-0x01EB.md) |
| `0x00` NOTI | `0x01F7` | `EntryAdmissionNoticeMessage`、`GameServerNoticeMessage` | 仅证明 encoder 可产生该 identity | [0x01F7](game/server-to-client/NOTI-0x01F7.md) |
| `0x00` NOTI | `0x021C` | `ExpertStoreClosedMessage` | 仅证明 encoder 可产生该 identity | [0x021C](game/server-to-client/NOTI-0x021C.md) |
| `0x00` NOTI | `0x021E` | `UsableCountBaselineMessage` | 仅证明 encoder 可产生该 identity | [0x021E](game/server-to-client/NOTI-0x021E.md) |
| `0x00` NOTI | `0x021F` | `BossTowerBaselineMessage` | 仅证明 encoder 可产生该 identity | [0x021F](game/server-to-client/NOTI-0x021F.md) |
| `0x00` NOTI | `0x023F` | `AcceptedQuestProgressMessage` | 仅证明 encoder 可产生该 identity | [0x023F](game/server-to-client/NOTI-0x023F.md) |
| `0x00` NOTI | `0x0245` | `KnightShieldDeckMessage` | 仅证明 encoder 可产生该 identity | [0x0245](game/server-to-client/NOTI-0x0245.md) |
| `0x00` NOTI | `0x025B` | `ChampionBreakStateMessage` | 仅证明 encoder 可产生该 identity | [0x025B](game/server-to-client/NOTI-0x025B.md) |
| `0x00` NOTI | `0x0286` | `DailyChallengeStateMessage` | 仅证明 encoder 可产生该 identity | [0x0286](game/server-to-client/NOTI-0x0286.md) |
| `0x00` NOTI | `0x0289` | `HonorLevelInfoNotice`、`HonorProgressStateMessage` | 仅证明 encoder 可产生该 identity | [0x0289](game/server-to-client/NOTI-0x0289.md) |
| `0x00` NOTI | `0x02D5` | `DailyScheduleStateMessage` | 仅证明 encoder 可产生该 identity | [0x02D5](game/server-to-client/NOTI-0x02D5.md) |
| `0x00` NOTI | `0x0331` | `GoldLimitStateMessage` | 仅证明 encoder 可产生该 identity | [0x0331](game/server-to-client/NOTI-0x0331.md) |
| `0x00` NOTI | `0x0344` | `PurchaseRestrictionCompatibilityMessage` | 仅证明 encoder 可产生该 identity | [0x0344](game/server-to-client/NOTI-0x0344.md) |
| `0x00` NOTI | `0x036A` | `CubeProgressionStateMessage` | 仅证明 encoder 可产生该 identity | [0x036A](game/server-to-client/NOTI-0x036A.md) |
| `0x00` NOTI | `0x0381` | `CollectionBoxStateMessage` | 仅证明 encoder 可产生该 identity | [0x0381](game/server-to-client/NOTI-0x0381.md) |
| `0x00` NOTI | `0x03C1` | `RentalStateMessage` | 仅证明 encoder 可产生该 identity | [0x03C1](game/server-to-client/NOTI-0x03C1.md) |
| `0x00` NOTI | `0x03D8` | `LotteryProgressStateMessage` | 仅证明 encoder 可产生该 identity | [0x03D8](game/server-to-client/NOTI-0x03D8.md) |
| `0x00` NOTI | `0x0465` | `EpicBookBaselineMessage` | 仅证明 encoder 可产生该 identity | [0x0465](game/server-to-client/NOTI-0x0465.md) |

<!-- rks:server-coverage:end -->

## 2026-09-21 复活币实现候选

CMD [0029 USE_COIN](game/client-to-server/0x0029.md)：请求 2B，成功 ACK 4B；事务扣付款人一枚币、NOTI 0020 state=1 广播并取消全灭计时。ACK flag 首轮取 1，业务值与实机复活尚待验证，不标记 ClientVerified。

## 虚弱投影（2026-09-21）

[NOTI 0x0021](game/server-to-client/NOTI-0x0021.md)：登录 32/100 单值实验经用户实机确认；已接内存虚弱与自然恢复，完整生命周期待用户实机验收。CMD 0009 付费恢复仍未实现。

- 2026-09-21：`0x0022` 转职任务奖励实现与候选17B特殊ACK，规则/事务测试通过；A21实机验收待完成，详见 [FINISH_QUEST](game/client-to-server/0x0022.md)。

- 2026-09-22：黄金翻牌第10次免费及额度用尽后的付费入口经用户验收；服务日志扣费0，持久化已用次数9→10，见 [SELECT_CARD](game/client-to-server/0x0047.md)。

### 2026-09-22 PREMIUM_SERVICE 使用上报

CMD036F category1/slot3（BUFF）、slot4（HP/MP恢复）使用上报已接入完整权威快照应答。slot3按PVF每日10次、同角色同实例最多消费一次；234已实测9→10、下一图无BUFF且保持10，ClientVerified。slot4不额外回血、不新增每日用量，回包后及再次入图恢复已用户验收；到期/跨日刷新仍为自动化验证。见[上报与应答](game/client-to-server/0x036F.md)。

## 2026-09-22 普通袖珍罐崩溃纠正

原13B开始包造成A21越界，撤回两阶段/00D9设计。普通模式0一次事务并返回62B结果；补齐两个嵌套读取字段（3B），真实读取载荷61/61。三种普通装备罐的显示、入包、数量及重登已由用户验收。[请求](game/client-to-server/0x001B.md)、[响应](game/server-to-client/CMD-0x001B.md)。7110/7120/7150基础闭环及#121随机开罐已ClientVerified；启动预加载1569种定义，按实际PVF权重抽取，六次随机实机提交成功。区间概率依自动化验证，契约模式和其他罐型未扩大验收。

## 2026-09-23 双倍开罐与续购验收

CMD001B mode1双倍显示/发奖、CMD0040购买后036F slot1刷新、每日累计30次后续购不恢复已ClientVerified。只读数据库最终累计30/本轮8；跨日与事务回滚由154项相关自动化测试覆盖。普通开罐与双倍仍使用62B成功应答。NOTI0353比较提示经0/30及29/30普通开罐差分验证，维持0/0兼容部署，文案语义问题留#122；不把0353解释为实际额度校验。上述30次业务为用户确认的本服规则。

> 2026-09-23 技能配置：领域支持N套及Reset/ResetAll，首次转职重置；CMD001D遗忘河之水退点与CMD0104双页切换候选实现完成，实机验收待执行。参见Document/技能配置与重置实施.md。

### 2026-09-24 技能槽位调整

`CMD 0x001C CHANGE_SKILLSLOT` 已接入：11B 实帧、校验、活动技能配置内事务交换、持久化和提交后 ACK。见[协议记录](game/client-to-server/0x001C.md)。状态：2026-09-24 用户确认四种拖放及重选角色后的布局持久化全部通过，普通 PVE 操作标记 ClientVerified。

### 2026-09-24 无色消耗与空槽投影验收

- [CMD 0x0012](game/client-to-server/0x0012.md)：无色技能正常消费账号材料、不再断线，用户实机验收通过。
- [NOTI 0x000E](game/server-to-client/NOTI-0x000E.md)：最后一瓶遗忘河之水消耗后立即清槽，用户实机验收通过。

上述具体路径标记 ClientVerified；未扩展到其他材料或容器场景。

### 2026-09-24 角色设置原样持久化（过渡态）

[CMD 0x01C0](game/client-to-server/0x01C0.md) 接入角色设置 blob 保存，NOTI 0187 登录回放。实现及自动化验证完成，待实机验收；内部逐字段语义解析由 [#129](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/129) 跟踪。

## 2026-09-25 邮箱基础闭环补充

005E/013B发送、0144收件人查询、0060打开、005F逐附件领取、0086已读与幂等删除已接入；NOTI0061用于登录/打开投影，0063用于在线提醒。用户已验收文字、装备、材料、金币与混合附件正常收发领取；不将未测试拒绝提示、非ASCII姓名、时装/宠物、保存/退回升级ClientVerified。逐消息人工区和 `Document/tasks/mailbox-protocol/verification/closeout-20260925.md` 提供证据边界。

2026-09-25：NOTI0063双账号双向在线通知、保留旧邮件后再次收信提醒均由用户实机验收，具体行为标ClientVerified；计数口径仍未差分定案。

## 2026-09-26 晶之契约选择接入

CMD [0218](game/client-to-server/0x0218.md)：选择保存、3B成功ACK及036A即时投影已实现并通过自动测试。NOTI [036A](game/server-to-client/NOTI-0x036A.md)复用登录投影。ClientStatic/ServerImplemented，战斗消费待实机，未标ClientVerified。
