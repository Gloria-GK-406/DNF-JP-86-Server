# 赫拉斯研究所：70 二进制与 S4A21 上游首轮取证

日期：2026-10-16。范围：只读本地指定 70 二进制和 ServerS4A21 源码；未运行服务、测试或客户端，未修改生产文件。证据不等于当前 JP86 实机验证。

## 结论

- **70Reference / Confirmed（有界阴性）**：指定的 70 `df_game_r` 未找到 TimeSpiral、BreakTrap 或赫拉斯研究所专用实现的符号/ASCII 字符串线索。不能据此宣称所有 70 服务端都绝对没有；本批二进制不适合作为这项活动完整行为的直接真源。
- **UpstreamReference / Confirmed（源码）**：S4A21 有真实接线的 TimeSpiral 机制：陷阱命令应答、随机传送选点、条件门通知、移动目的地覆盖、隐藏 Boss 跟踪和通关请求。
- **UpstreamReference / Confirmed（限制）**：随机 Buff 只抽取配置并记日志，没有落地发包/属性效果；陷阱请求 Payload 未做字段级解析。因此不能声称上游已经完整实现、可直接照搬。
- **交叉证据 / Confirmed**：当前 DGN 3900 没有 `[time spiral]` 标签，按上游识别逻辑不会启用专用链；上游陷阱成功应答仅 1 字节，而当前 A21 静态响应读取还要求 X/Y/result 3 字节，是明确短包风险。

## 70 二进制证据

对象：`E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r`。

本轮 SHA256：`CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`，与派发提供的历史哈希一致。

脚本 `70/collect.py` 直接读取 ELF32 little-endian `.symtab`；共有 97,021 个带函数类型的不同 VA。先检索 `timespiral|heras|break.?trap|trap.?buff`，再扩大到 `trap|spiral|laborat|heras|hellas`。扩大结果只有两条：

| VA | 符号 | 判断 |
| --- | --- | --- |
| `0x080972FE` | `_ZN6CipheraSERKS_` | 正则 heras 命中 CipheraSER 子串，8 字节赋值函数，非活动 |
| `0x081C4B7A` | `_ZN18DisPatcher_MoveMap20CheckTrapBitsHackCntEP5CUserR12MSG_MOVE_MAP` | 普通 MoveMap 反作弊计数检查，428 字节；非赫拉斯 |

后者反汇编 `70/081c4b7a.asm.txt` 显示调用 `CUser::getHackAnalyzer`、`CHackAnalyzer::addServerHackCnt`（hacktype `0x1F9/0x1FA`）、`getServerHackAccumulatedCnt`、`DB_ReqSaveHackUserPunish::makeRequest`、`CUser::DisConnSig`；累计超阈值后检查队伍成员并处罚。未发现传送配置、Buff 或活动通关逻辑。

另外逐条扫描全文件长度至少 6 的 ASCII 可打印串，用 `time.?spiral|break.?trap|trap.?buff|global_timespiral|heras|hellas|laborat` 搜索，唯一结果仍是上面的 Cipher 符号误命中。工件见 `70/symbols.json`、`70/strings.json`。没有对无符号匿名函数、非 ASCII 文字或全部间接调用做穷举，不能把有界搜索提升为绝对不存在证明。

## S4A21 上游身份与主要链路

仓库：`E:/Project/DNF/ServerS4A21`，HEAD `c20dba5fa52c2faf4b5087a3b477676be8434e8f`。读取时 `master...origin/master`，只有既存未跟踪 `_reference-backup-20260826/`。没有 `.codegraph/`，因此按文件和符号检索。

以下路径均相对该仓库：

| 行为 | 当前源码位置 | 确认内容 |
| --- | --- | --- |
| 副本识别 | `Server/DfoServer/GameWorld/TimeSpiralDungeonData.cs:66` | dungeon PVF 存在 `[time spiral]` 标签即识别，不按显示名硬编码 |
| 数据来源 | 同文件 `:261`、`:325`、`:385` | 读取 `etc/global_timespiral.etc`（带大小写回退） |
| 传送选择 | 同文件 `:78`、`:261` | `[teleport trap]` 以当前 trap map id 索引；`[move list]` 每 4 整数为 X、Y、Weight、Flag；正权重加总抽取，全无正权重时回退首项 |
| Buff 选择 | 同文件 `:196`、`:325` | `[trap buff rate]` 为 index/weight 对，`[trap buff]` 读物攻、魔攻、移动/攻击/施放速度和持续毫秒；只接受正权重且至少一个非零属性 |
| 命令入口 | `Server/DfoServer/Network/Parsers/Dungeon/DungeonCommandParser.cs:50`；`Network/Handlers/Dungeon/DungeonCommandReceivedDispatcher.cs:63` | 将完整 body 包装为 `BreakTrapResultDungeonCommand`；未限制长度或解析字段 |
| 陷阱推进 | `Server/DfoServer/Network/Handlers/Dungeon/TimeSpiralDungeonCoordinator.cs:29` | 先发成功 ACK，再验证 run/room identity、当前参与关系和 TimeSpiral；当前房间 map id 决定随机目标；拒绝 already_pending |
| 条件门通知 | 同文件 `:384`；`Network/Handlers/Dungeon/DungeonMechanismNotificationSender.cs:47` | 发送 COMPLETE_CONDITION_PASS_GATE |
| 移动消费 | `TimeSpiralDungeonCoordinator.cs:77`；`DungeonMechanismCoordinator.cs:142` | 仅 pending 且当前 run 合法时消费随机目标，清 pending 和旧隐藏 Boss，再覆盖普通 MoveMap 的目的地；同时抽取 Buff |
| 队伍状态 | `TimeSpiralDungeonCoordinator.cs:145` | 拷贝 leader 的目标状态，member pending=false；不能等同已经实机验证队伍传送 |
| 隐藏 Boss 注册 | 同文件 `:162`、`:279`；`GameWorld/TimeSpiralDungeonData.cs:156` | 最终 Boss 房或当前 flag=0 传送目标房可注册；逆序查找 `NewMonsters/Timespiral/mv_771_boss/` 怪物，跳过 actor type=9；绑定房间 map/坐标、sequence id 与 monster code |
| 击杀通关 | `TimeSpiralDungeonCoordinator.cs:248`；`DungeonMechanismCoordinator.cs:292` | 同房间且 sequence/code 全匹配的隐藏 Boss 击杀产生 `ClearRequest(shouldClearDungeon: true)`，交共享通关链处理 |
| 房间恢复 | `TimeSpiralDungeonCoordinator.cs:224` | 从既存 RoomState 恢复隐藏 Boss 跟踪事实，未见专属 durable DB 状态 |

## 上游协议线索和限制

- `Network/Core/PacketTypesA21.cs:2263`：CMD `BREAK_TRAP_RESULT = 0x0408`。
- 同文件 `:319`：NOTI `COMPLETE_CONDITION_PASS_GATE = 0x0138`。
- `Network/Builders/Dungeon/SpecialDungeonNotificationBuilder.cs:91`：条件门 body 是 `i32 0 + u8 0`，5 字节。其注释引用 A21 客户端 `0x01193290` 消费 i32/u8、通过 `0x0171C5A0` 设置条件完成，GateKeeper `0x01BC6E30` 读 `0x0171C590`。**这是上游代码内的客户端逆向引用，本轮未对 dump 重新反汇编，不能提升为当前 ClientStatic 独立确认。**
- `SelfTests/A21SpecialDungeonProtocolSelfTest.cs:253` 断言上述 5 字节；本轮未执行测试，也未找到 TimeSpiral 专属 SelfTest。
- `TimeSpiralDungeonCoordinator.cs:296-325` 的 `LogDeferredBuff` 只输出 `packet=deferred reason=no_confirmed_buff_id`，没有 Buff 发包/属性修改。
- `GameWorld/TimeSpiralDungeonData.cs:118` 有对象路径验证 helper（Timespiral gate/Trap.obj），本轮全源码搜索没有找到调用它的地方；当前 BreakTrap 链按 map id 推进，不能声称已经验证具体对象来源。
- Trap ACK 在合法性检查前发送；这不等于允许任意请求修改状态，但异常/重复请求的玩家行为需要后续明确设计与回归。
- **成功 ACK 短包风险**：`TimeSpiralDungeonCoordinator.cs:36-39` 调 `CommonPacketBodyBuilder.BuildSuccessAck()`；`Network/Builders/CommonPacketBodyBuilder.cs:7-10` 明确返回 `{ 0x01 }`，仅 1B。本轮交叉读取 A21 子任务 `a21.md:35-47`：响应 handler VA `01107500` 在 success 后继续读 `u8 X + u8 Y + u8 result`，完整成功体至少 4B `[1, X, Y, result]`。因此上游响应缺少 3B，不能直接移植；result 的确切语义仍不可凭非零/零分支猜测。该判断为上游源码与本轮 ClientStatic 工件交叉结论，未宣称 WireCaptured 或 ClientVerified。

## 可以复用的知识与仍待确认内容

### 当前资源拓扑差异的移植风险

主代理转交 PVF 调查发现：当前服务端 DGN 3900 为 3×3 且选图池不含 7 个 trap MAP，而 global ETC 仍有 5×5 坐标和 7 个 trap MAP。本段资源事实由 PVF 子任务提供，本子任务仅检查上游如何消费它。

- 上游没有硬编码 5×5；但它**隐含要求 DGN 当前 maze 的实际地图 ID 与 ETC trap 键、目标坐标一致**。`TimeSpiralDungeonCoordinator.cs:416` 从当前 RoomState 读取实际 `Maze.Index`，`:356` 用该 map id 查 ETC dictionary；并无按副本号切换配置、坐标缩放、3×3/5×5 转换或自动生成陷阱房。
- **更早的识别门就不满足**：本轮交叉读取 `pvf/dungeon3900.txt` 和 `pvf/client-dungeon3900.txt`，只有头部 `GTimeSpiral/TimeSpiral.dgn` 资源路径包含 TimeSpiral，没有 `[time spiral]` 标签。上游 `TimeSpiralDungeonData.cs:70` 要求 `Root.GetChild("time spiral") != null`，并不读取路径。因此按现行资源直接运行上游代码，DGN 3900 会被判定为非 TimeSpiral；随机传送和专属隐藏 Boss 通关链均不会启用。不能以代码中存在 Coordinator 推导现行资源已经支持。
- 因而如果现行可进入房间没有 ETC 指定 trap map，`TryPickTeleportTarget` 会失败，陷阱链没有随机目标，不能通过“接上 0408”自动补齐。
- 目标坐标仅验证 X/Y 非负（`:100`），未核对目标是否存在于当前 maze。`DungeonMapHandler.cs:158-181` 先对**普通请求**做 `DungeonRoomTopology.TryResolveMoveTarget`，随后 TimeSpiral **覆盖**目的地；在同方法下一段直到 `SendStartMapAsync`（`:223`）没有再次调用 topology 校验。把 5×5 ETC 目标直接应用到 3×3 DGN 有越界或不存在房间的风险，也可能在后续失败前已经消费 pending。
- 首期移植应先确定现行资源究竟采用哪个版本布局；保留现行 3×3 时，只能使用真实可达房间和现有机制，不应合成旧 5×5 路线；恢复旧版路线属于资源/玩法范围的额外决策，不能仅复制上游 Coordinator。确认目标存在后才消费 pending，是实现时需要补足的约束。

可借鉴：从资源标签识别活动、从 ETC 权重选目标、把传送选定与 MoveMap 消费分开、以 run/room 身份阻止旧请求串局、按房间与序号绑定隐藏 Boss、复用共享 Clear/Reward 流程。

仍为 Unknown：本期 JP86 资源中的具体副本/地图/怪物/入场券 ID 与开放规则（交由 PVF 调查）；`0408` 当前 A21 实际 body、触发先后和失败应答；Flag 除 0 外的语义；Buff 的正确通知 ID、字段和实际属性结算；隐藏 Boss 随机路线的客户端接受行为；门条件与具体 passive object 的关系；断线恢复、重启和奖励重复保护；正式奖励/掉落/次数规则。需要当前 ClientStatic/WireCaptured/ClientVerified 逐级补证，不能用上游源码替代。
