# 安图恩攻坚进行中：上游参照调研

2026-11-19。范围：开团完成之后、攻坚结束之前（态势、阶段、计时、机制、地图开闭、各小队进图与结果回报、失败、断线重入）。招募、编队与开团前流程、奖励结算与翻牌由其他调研负责，本文只在交界处点到。本文没有启动任何服务端，也没有 A21 实机样本。

## 0. 约定

**来源标签**

| 标签 | 含义 |
|---|---|
| `UpstreamStatic` | ServerS4A21 提交 `1870c85` 的源码与自测。只是线索，不能证明 A21 行为 |
| `ThirdPartyStatic` | 第三方商业端 v3.3 反编译。比上游新，只用来对比差异 |
| `ClientStatic` | A21 客户端静态读出（JP86 协议页机器区，客户端 SHA-256 `1F8244D3…`），以及客户端 PVF 资源声明（标为 `ClientStatic(PVF)`） |
| `Unknown` | 没有可用证据，待实机 |

**路径缩写**

| 缩写 | 实际位置 |
|---|---|
| `U/` | `E:\Project\DNF\ServerS4A21\Server\DfoServer\` |
| `UT/` | `E:\Project\DNF\ServerS4A21\Tool\PvfLib\Models\` |
| `T/` | `E:\Project\DNF\Research\third-party-s4a21-v3.3\decompiled\DfoServer\` |
| `J/` | 本 worktree `E:\Project\DNF\DNF-JP-86-Server-anton-raid\` |
| `PVF:n` | dev 通道 Script.pvf（SHA-256 `A97132DD…`）`etc/raid/anton.etc` 经 PvfProbe `dump` 输出的第 n 行。`PVFbuff:n` 是 `etc/raid/anton.etc.buff` 的对应行 |

PVF 用现有构建好的 PvfProbe 读取：`dotnet …\DNF-JP-86-Server\Tool\PvfProbe\bin\Release\net10.0\PvfProbe.dll dump etc/raid/anton.etc 20000`（`PVF_ARCHIVE_PATH` 指向 dev 挂载）。工作区另一份调研 `Document/安图恩团本实现与A21证据-20261119.md` 已比较过三份 PVF，进行中部分无差异。

**攻坚状态值**（`RaidSnapshot.State` / `StateArgument`，`NOTI 0x024C` 两个字节）`UpstreamStatic`

| State | Arg | 含义 | 出处 |
|---|---|---|---|
| 0 | 0 | 招募；`StartPending` 表示开团准备中 | `U/Game/Raid/RaidManager.cs:509-522` |
| 2 | 0 | 一阶段攻坚 | `RaidManager.cs:602-626` |
| 2 | 1 | 二阶段攻坚（Arg 兼作阶段号） | `RaidManager.cs:1234-1263` |
| 3 | 0 | 阶段结算中（动画、翻牌） | `RaidManager.cs:1074-1095` |
| 5 | 0 | 一阶段与二阶段之间的间歇待命 | `RaidManager.cs:1167-1184` |
| 4 | 0 | 二阶段奖励完成后的终态 | 同上 |
| 4 | 1 | 超时失败终态；此时不发 `0x024C`，改发失败 `RAID_RESULT` | `RaidManager.cs:1097-1122`、`U/Network/Handlers/RaidHandler.cs:388-392` |

第三方在二阶段开打时额外发 `RAID_STATE(1,1)` 再发 `(2,1)`（`T/DfoServer.Network.Handlers/RaidHandler.cs:3287-3311`），上游从不发 State 1。`ThirdPartyStatic`

## 1. opcode 总表

收件人「全团」= `RaidSnapshot.Members` 里每个角色的当前会话（`U/Network/Handlers/RaidHandler.cs:394-398`，经 `_sessions.BroadcastToAsync`），不区分是否在副本中。ACK = `cmd=1` 同 type、1 字节 `01/00`（`RaidHandler.cs:413-416`）。

### 1.1 客户端 → 服务端

| type | 名称 | 上游 handler | 上游读取的 body | 触发与处理 | 与 JP86 机器区对照 |
|---|---|---|---|---|---|
| `0x0294` | RAID_MOVIE_SKIP | `U/Network/Handlers/RaidHandler.Rewards.cs:267-300` | `u8 flag`，`flag==1` 表示动画播完/跳过 | 只在 State 3 且结算流程已开始时接受；回 ACK；`flag==1` 时开始翻牌并单发 `RAID_STATE(4,arg)` 与小队奖励列表（翻牌交给奖励调研） | 机器区 `BodyLength=1` 闭合，客户端注册了 CMD 响应 handler，ACK 合理。一致 |
| `0x0296` | RAID_DO_BEHAVIOR | `RaidHandler.cs:192-214` | 要求 **恰好 8 字节**：`u32 target` + `u32 behavior` | State 2 时把原始 body 广播成 `NOTI 0x0247`，发给全团（含发送者）；回 ACK | **不一致**：A21 [请求构包](../../../../protocol/game/client-to-server/0x0296.md) 闭合为 `u32 + u16` 共 6 字节，`NOTI 0x0247` 也读该布局。上游的 8 字节长度校验不接受此请求，A21 未注册同号 CMD 响应 handler |
| `0x0297` | RAID_SET_SYMBOL | `RaidHandler.cs:216-263` | 9 字节：`u32 symbolId`、`u32 operand`、`u8 op`（0 赋值、1 加且饱和、2 减且下限 0） | 只接受二阶段、symbol 110（黑色火山屏障）、且本团已登记该符号；走屏障变更（见 §2.4）；回 ACK | 布局一致（机器区闭合 9 字节）。客户端未注册响应 handler，ACK 同上存疑。上游只放行 110 是它自己的限制，A21 是否还会上报其他符号 `Unknown` |
| `0x02B7` | RAID_BUFF_SYSTEM | `U/Network/Handlers/RaidHandler.Situation.cs:28-131`、`:257-279` | `u8 buffType`；可选 `u16 partyIndex`；其后到包尾每 2 字节一个目标 userId | State 2 才接受；按冷却登记激活；`INCREASE TIME` 延长阶段时限、`INCREASE COIN` 给目标小队返还复活币额度；之后广播 `NOTI 0x027A`，延时成功时再广播 `0x0255/0x0256` 并重排进攻计时；回 ACK | 字段一致（机器区 `3 + n×2` 下界）。客户端未注册响应 handler，ACK 存疑。上游**不检查施放者是否团长**，PVF 未标明施放权限，`Unknown` |
| `0x02B8` | RAID_MONSTER_HP | `RaidHandler.Situation.cs:133-189` | `u8 n`（≤5），随后 `n×u32`，要求 `len == 1+4n` | `n==0`：补登参与（迟到的入图回报），单发攻坚对象、参与快照、buff 状态、怪物状态。`n>0`：按发送者所在小队与当前地图缓存这组值，再向全团广播 `NOTI 0x027B` | A21 [三个构包点](../../../../protocol/game/client-to-server/0x02B8.md) 均符合该计数布局，含 `n=0`；限值与零值触发的服务端处理属于上游策略。值的含义上游未解释；结合 `PVFbuff:29-128` 每图最多 1 Boss + 3 精英，推测为这些怪的血量，`Unknown` |

### 1.2 服务端 → 客户端

| type | 名称 | 上游构包（`U/Network/Builders/Raid/RaidPacketBuilder.cs`） | 布局 | 发出时机与收件人 | 与 JP86 机器区对照 |
|---|---|---|---|---|---|
| `0x0247` | RAID_DO_BEHAVIOR | 不构包，原样转发 `0x0296` body（`RaidHandler.cs:203`） | 上游 8 字节 | 全团 | 机器区读 `u32 + u16`，与上游 8 字节冲突（同 `0x0296`）`ClientStatic` |
| `0x0248` | RAID_SET_SYMBOL | `BuildSetSymbols` `:397-416` | `u8 n` + `n×(u32 symbolId, u32 value)` | 全团；每次机制变化 | 一致（机器区 `1 + n×8`）。机器区「可能含义」一列把组内首字段写成 u8 符号 ID，应为 u32 |
| `0x0249` | RAID_DUNGEON_PARTICIPATION_INFO | `BuildRaidDungeonParticipationInfo` `:467-486` | `u8 1`、`u32 dungeonId`、`u8 op`、`u8 n`、`n×u16 userId` | 全团。op：入图时连发 0 再 1；离图 0；通关 4；补发快照时 0、2，已通关再加 4（`RaidHandler.Situation.cs:418-470`） | 一致（机器区 `u8,u32,u8,u8,u16`）。op 0/1/2/4 的含义只来自上游命名，`Unknown` |
| `0x024A` | RAID_DUNGEON_STATE | `BuildDungeonState` `:418-452` | `u8 0`、`u8 n`、`n×(u32 dungeonId, u8 state)`、`u32 infectionDungeonId` | 全团；阶段开始整表下发，孵化中心开启/感染变化时下发子集 | **待核**：机器区按读取顺序列为 `u8, u8, u32, 组(u32,u8)`，把 u32 放在组前；上游与第三方都把感染图号放在包尾（第三方按 `6 + n×5` 校验长度，`T/…/RaidHandler.cs:3080-3091`）。静态顺序可能只是读取指令顺序，`Unknown` |
| `0x024B` | RAID_CHANGE_DUNGEON_STATE | `BuildChangeDungeonState` `:454-465` | `u32 dungeonId`、`u8 0`、`u8 state` | 全团；单图状态变化 | 一致（机器区 `u32,u8,u8`）。机器区证据列写成 `WriteUInt32(0)`，实为 1 字节 |
| `0x024C` | RAID_STATE | `BuildRaidState` `:292-306` | `u8 state`、`u8 arg` | 全团；阶段切换、开团与准备取消 | 一致。机器区「超时失败写 3」是旧版上游；现版超时改发 `0x025A` |
| `0x024E` | RAID_MEMBER_STATE（相关） | `BuildRaidMemberState` `:488-494` | `u16 userId`、`u8 state` | 全团；攻坚图内角色死亡发 0，复活币复活发 1（`RaidHandler.Progression.cs:188-211`） | **不一致**：协议页写 1=存活、2=死亡，引用的是旧版上游；现版死亡发 0。第三方已删掉这两次发送（`T/…/RaidHandler.cs:3603-3620`）。`Unknown` |
| `0x0255` | RAID_SET_TIMER | `BuildSetTimer` `:319-337` | `u8 timerType`、`u32 dungeonId`、`u32 endUnixTs`、`u32 durationSeconds` | 全团；每次启动或重排计时器 | 一致（机器区 `u8,u32,u32,u32`）；后两个 u32 谁是截止时刻、谁是时长只来自上游，`Unknown` |
| `0x0256` | RAID_REMAIN_TIME | `BuildRemainTime` `:339-345` | `u8 kind`（0 进攻/准备，1 阶段间歇）、`u32 remainSeconds` | 全团；与类型 0 的 `0x0255` 成对 | 一致 |
| `0x0258` | RAID_MOVIE_SKIP | `BuildRaidMovieSkip` `:362-368` | 上游 `u32 movieId`、`u32 option`，只发 `(0,0)` | 阶段结算开始 2 秒后，若还没开始翻牌，发给全团（`RaidHandler.Rewards.cs:327-338`） | 机器区「结构未知」（进入打包区）。上游布局无客户端佐证 |
| `0x0273` | CLEAR_RAID_BUFF | 无 | — | 上游与第三方都**从不发送** | 机器区「结构未知」。何时需要发、清的是什么，`Unknown` |
| `0x027A` | RAID_BUFF_SYSTEM | `BuildRaidBuffSystem` `:517-543` | `u8 组数`；每组 `u8 buffType`、`u8 n`、`n×(u16 partyIndex, u16 userId, u32 activeUntilTs, u32 cooldownUntilTs)` | 全团；开团、二阶段开始、施放 buff 后；补发快照时单发。上游固定 5 组，客户端类型顺序 `2,3,4,1,0`（`RaidHandler.Situation.cs:191-255`） | 一致（机器区读出首组首条 15 字节） |
| `0x027B` | RAID_MONSTER_HP | `BuildRaidMonsterHp` `:545-569` | `u8 组数`；每组 `u16 partyIndex`、`u8 n`、`n×u16 userId`、`u32 usedCoin`、`u8 m`、`m×u32 值` | 全团；入图、通关、离图、复活、改编、buff 后；每个「小队组」一条，包括未出战的组 | 一致（机器区 `11 + n×4` 以单成员为例）。上游字段名 `SituationIndex` 实际写的是 `PartyIndex` |
| `0x028A` | RAID_INOUT_SYSTEM | 无 | — | 上游与第三方都**从不发送** | 机器区只读出首个 `u32`。语义 `Unknown`；名字像成员进出攻坚的通知 |
| `0x04BD` | RAID_USER_FINISH_LOAD | `U/Network/Handlers/TownHandler.cs:1745-1749` | 空 body | 只在攻坚频道的城镇到场（团本城镇 19）时发给本人；攻坚进行中的进出图不发 | 机器区「结构未知」，工作区证据文档记录客户端 handler 不读 body。与本范围的关系只在于：回城后是否要再发一次 `Unknown` |

**攻坚中复用的普通副本帧** `UpstreamStatic`

- 选图 `0x000F`、进图 `0x0010`：攻坚频道里只有攻坚处于 State 2 才放行，**不检查**图号是否属于当前阶段、地图是否开放、是否超过同图小队上限（`U/Network/Handlers/Dungeon/DungeonEntryHandler.cs:619-627`、`:1127-1148`、`:2080-2108`）。拒绝时用普通入场拒绝帧加一条限制公告。帧本身没有攻坚特殊值。
- 读图完成 `FINISH_LOADING`：照常走普通加载闸门，之后调用攻坚的「已进图」回报（`U/Network/Handlers/Dungeon/DungeonLoadingCoordinator.cs:46-69`）。
- 结算 `0x002E SET_PLAY_RESULT`：通关后若是攻坚图，**取消翻牌**（清空 `CardRewards`、直接置 `CardsRevealed`），再回报攻坚通关（`U/Network/Protocol/GameProtocolHandler.cs:700-724`）。
- 死亡 `0x0028`、复活币 `0x0029`、退出 `0x0048/0x007B/0x0084/0x002A`：普通处理后追加攻坚回报（`GameProtocolHandler.cs:743-779`、`:791-805`、`:862-865`）。
- `NOTI 0x001F ENABLE_CLEAR_DUNGEON`：阶段结束或攻坚失败时，发给仍在攻坚图里的成员，以及一阶段完成后已回城且有通关记录的成员（`RaidHandler.Rewards.cs:790-808`）。
- 奖励策略 `DungeonRewardPolicy.AntonRaid`：保留怪物经验与任务进度，关闭怪物掉落与任务掉落（`U/Game/Dungeon/DungeonRewardPolicy.cs:24-32`）；攻坚图不生成随机精英（`U/GameWorld/Dungeon.cs:232`、`:275`）。注意 `DungeonRewardPolicyData.IsAntonRaidDungeonId` 只认 `210–215, 218–224`，**漏了舰炮 216**（`U/GameWorld/DungeonRewardPolicyData.cs:65-69`），而攻坚逻辑的 `IsAntonRaidDungeon` 包含 216（`RaidHandler.Runtime.cs:475-495`）。上游自身不一致。

**双人 raid**：`DUAL_RAID_*`（CMD `0x0205/0206/0216/0217`，NOTI `0x01C8–0x01DB`、`0x0419/041A`、CMD `0x0432`）在上游和第三方都只有枚举、没有 handler，枚举位置（456–475）远早于攻坚族（583 起），有独立的进图、Boss 图移动与结算帧。判断不属于安图恩攻坚。`UpstreamStatic` + `ThirdPartyStatic`

## 2. 安图恩内容定义

### 2.1 全局参数 `ClientStatic(PVF)`

| PVF 字段 | 值 | 行 | 上游是否使用 |
|---|---|---|---|
| `PHASE MAX` | 2 | PVF:2-3 | 硬编码两阶段 |
| `RAID MEMBER MAX` / `RAID START MINIMUM MEMBER` | 20 / 10 | PVF:4-7 | 只用 20；最低人数未检查（第三方检查，`T/…/RaidHandler.cs:3385-3391`） |
| `DISCONN MEMBER WAIT TIME` | 300 | PVF:12-13 | **未使用**，掉线即离团（§4.5）；第三方实现 |
| `START DELAY TIME` | 3 | PVF:14-15 | 用于开团与二阶段准备倒计时（`U/Game/Raid/AntonRaidRewardProvider.cs:116-120`，缺省 3） |
| `PHASE TIME OVER` | 2400 2400 | PVF:16-17 | 阶段时限（`AntonRaidTimerConfiguration.cs:167-183`，缺省 2400） |
| `PHASE BREAK TIME` | 600 | PVF:18-19 | 间歇，上游实际排 `600-5=595` 秒（`RaidHandler.Rewards.cs:780-788`） |
| `RETRY` / `RETRY ITEM` | 1 / `10099897 ×1` | PVF:23-26 | **未实现** |
| `SHIELD CHARGE RATE` | 17 34 | PVF:27-28 | 屏障每秒回复量：普通 17、有感染时 34（`AntonRaidRewardProvider.cs:128-137`）。「每秒」是上游假设 |
| `HATCHERY INFO` | 共 4 开 3：221 222 223 224 | PVF:29-31 | 用于随机开孵化中心 |
| `EXCEPT CHEAT DUNGEON` | 220 | PVF:32-34 | 读了但无人调用（`AntonRaidRewardProvider.cs:108-114`）。含义 `Unknown` |
| 各图 `PARTY MAX` | 216 为 1，其余 5 | PVF:80-81、146-147 等 | **未使用**。推测是同一地图同时可容纳的小队数 |
| 各图 `HP INFO` | 210:4，213/215:2，220:5，其余 1 | PVF:77-79 等 | 上游硬编码为通关次数阈值（`RaidHandler.Runtime.cs:510-524`），并用同名符号显示进度 |

### 2.2 地图与阶段 `ClientStatic(PVF)` + 名称取自 `dungeon/dungeon.lst`

| 阶段 | 图号 | 名称 | 初始 | 所需通关数 | 作用 |
|---|---|---|---|---|---|
| 一「阻截安徒恩」 | 210 | 黑雾之源 | open | 4 | 4 次后黑雾散去，开放其余战区 |
| | 211 | 黑雾之源（再生） | hide | 1 | 黑雾再生，失守即一阶段失败 |
| | 212 / 214 | 震颤的大地 A / B 战区 | hide | 1 | 限时守住，失守即一阶段失败；通关后关闭 150 秒再开 |
| | 213 / 215 | 擎天之柱 A / B 战区 | hide | 2 | 两者都清完即一阶段成功 |
| | 216 | 舰炮防御战 | hide | 1 | 黑雾散后 20 秒开放，最多 1 支小队；限时失败会重置震颤与擎天 |
| 二「灭杀安徒恩」 | 218 | 能量阻截战 | open | 1 | 可反复通关（通关后立即重开、进度清零）；地图四角有通往孵化中心的「安徒恩的筋」机关 |
| | 219 | 黑色火山 | open | 1 | 有屏障；通关后关闭能量/火山/孵化中心，开放心脏 |
| | 220 | 安徒恩的心脏 | hide | 5 | 5 次即二阶段完成 |
| | 221–224 | 第一～第四孵化中心 | hide | 1 | 二阶段开始 180 秒后随机开 3 个，其一为感染源；通关后关闭 240 秒再开 |

### 2.3 PVF 的触发器脚本（本节最重要）

`anton.etc` 每阶段除地图表外是一串 `[TRIGGER]`/`[BEHAVIOR]` 对（PVF:149-583、705-1017）。词汇封闭，整份文件只用到下列元素：

| 类别 | 元素 | 例 |
|---|---|---|
| 条件 | `[CHECK DUNGEON STATE] id state`、`[ON CHANGE DUNGEON STATE] id`（边沿）、`[CHECK TIMER END] type id`、`[IF] [符号] 值`、`[ON CHANGE SYMBOL] [符号]`、`[2PHASE INIT]` | PVF:196-199、206-209、252-256、526-530 |
| 动作 | `[SET DUNGEON STATE] id state`、`[RESERVE DUNGEON STATE] id state 秒`、`[SET] [符号] 值`、`[SET TIMER] type id 秒`、`[RESET TIMER] type id`、`[OPEN RANDOM HATCHERY]`、`[CLEAR PHASE]` | PVF:157-178、771、582 |
| 地图状态字 | `open`、`close`、`hide`、`clear` | — |
| 计时类型 | 1 = 守图时限（到点失败）、2 = 关闭后重开、3 = 周期效果（黑雾/陨石生成、孵化场精英、孵化场开启） | 由脚本用法归纳 |

上游**只解析了计时与延时开放**（`UT/RaidEtcFile.cs:279-353`），其余逻辑全部在 handler 里手写（§3）。第三方同样手写。对照脚本，上游的手写逻辑与脚本基本一致，差异列在 §3.6。

### 2.4 PVF 符号与上游用法

dev PVF 的 [符号表](04-jp86-protocol-client-pvf.md#46-antonetcsymbolid--符号) 给出名称和 ID。上游用法见 `U/Network/Handlers/RaidHandler.AntonDefinitions.cs:7-141`，其中 111 未在 dev PVF 的表中定位。

| ID | PVF 名（推断） | 用法 |
|---|---|---|
| 1 / 2 | SMOKE PASSIVE CREATE / METEO PASSIVE CREATE | 211、216 周期效果到点置 1 |
| 3–6 | 221–224 HATCHERY FAIL | 孵化中心类型 1 计时到点置 1 |
| 7 / 8 | 感染存在 / 感染图号 | PVF 有对应符号，`0x024A` 的尾字段驱动它们 |
| 9–12 | 221–224 HATCHERY NAMED CREATE | 孵化中心类型 3 计时到点置 1 |
| 50–56 | 210–216 DGN HP | `id = 图号 - 160`，显示累计通关数 |
| 57–63 | 218–224 DGN HP | `id = 图号 - 161` |
| 102 | KILL ALL CHARACTER | 一阶段失败与舰炮失守时置 1 |
| 104 / 105 | 1PHASE FAIL / 1PHASE SUCCESS | |
| 106 | 2PHASE INIT | |
| 110 | BLACK VOLCANO BARRIER | 0–10000 |
| 111 | 火山开放态（上游自定） | 屏障破为 1 |
| 120 | DGN SMOKE OPEN | |
| 121–128 | MOVIE SMOKE IN / SMOKE OUT / NAVIGUN ON / SUCCESS / FAIL / BARRIER BREAK / BARRIER RECOVER / HATCHERY OPEN | 动画符号；第三方补发快照时排除 121–128（`T/…/RaidHandler.cs:6609-6616`） |

**屏障 110**：二阶段开始置 10000（PVF:718-719）。客户端发 `0x0297` 改值（上游推测是能量阻截战里的攻击扣减）。降到 0 时置 `127=0,110=0,111=1,126=1` 并开始每秒回复；回满 10000 时置 `126=0,110=10000,111=0,127=1`；中间值只发 110（`RaidHandler.Progression.cs:463-552`）。回复期间**每秒向全团广播一次**。进入 219 时单发当前屏障快照（`:561-576`）。

### 2.5 地图状态字节

上游与第三方都只用数字。`0 = open`、`2 = hide` 可由初始表确定（`AntonDefinitions.cs:48-57`，对应 PVF 的 open/hide）。`1` 与 `3` 存在两种读法：

- 上游黑雾散后把 211 置 3（PVF 写 `close`，PVF:160），通关时把地图置 3（PVF 写 `clear`），所以上游把 3 同时当 close 和 clear 用。
- 第三方舰炮通关时先发 3 再发 1（`T/…/RaidHandler.cs:3683-3684`），PVF 此处是 `clear` 后 `close`（PVF:296-301），即 3=clear、1=close。

A21 的 `getStringToDungeonState` 与 NOTI 写入路径确定 `0=open、1=close、2=hide、3=clear`。JP86 按 [客户端枚举](../../../../protocol/game/server-to-client/NOTI-0x024B.md) 区分关闭与通关。

## 3. 时序

步骤用上游代码顺序列出；「全团」同 §1。所有延时与计时都绑定攻坚实例 ID、阶段号和计时版本（§4.3）。

### 3.1 一阶段开打（开团准备完成之后）

交界：`START_RAID`、准备倒计时、扣票由开团调研负责。准备完成回调 `CompleteRaidPreparationAsync`（`RaidHandler.Progression.cs:785-839`）在扣票成功后：

1. 全团：攻坚对象（`RAID_MODIFY`）、成员表、`RAID_STATE(2,0)`。
2. 全团：`0x024A` 一阶段初始表：210 open，211–216 hide，感染 0。
3. 全团：`0x027A` 空 buff 表、`0x027B` 怪物表（尚无人进图，组都无地图）。
4. 全团：`0x0248` 初始符号：50–56、1、2、120 全 0。
5. 全团：`0x0255(0,0,2400)`、`0x0256(0,2400)`，排进攻超时计时。

第三方用 3120 秒（2400×1.3）并先发 `RAID_STATE` 再发对象（`T/…/RaidHandler.cs:4230-4238`）。`ThirdPartyStatic`

### 3.2 小队选图、进图、读图

1. 小队在团本城镇（19）走普通选图/进图。上游只核对攻坚处于 State 2（§1.2）。
2. 每个客户端读图完成 → 普通加载闸门 → `HandleDungeonLoadedAsync`（`RaidHandler.Progression.cs:97-116`）：图号属于本阶段、角色还没登记参与时，`RaidManager.TryEnterDungeon` 以**该角色在攻坚编成里的整支小队**登记一条参与（`RaidManager.cs:731-792`），同队后来者因已在名单里被跳过。
3. 等 300 ms 后复核会话与本局仍有效，向全团发：（进 219 时先单发屏障快照）`0x0249(op=0)`、`0x0249(op=1)`、`0x027B`。
4. 小队名单取自攻坚编成而不是实际进图的人，所以没进图的队友也被算作参与。

迟到补登：客户端发 `0x02B8 n=0` 时也会尝试登记（`RaidHandler.Situation.cs:406-416`）。

### 3.3 地图通关

`0x002E` 结算触发，且本局刚从 Cleared 进入 ResultShown（`GameProtocolHandler.cs:700-724`）→ `HandleDungeonClearedAsync`（`RaidHandler.Progression.cs:118-186`）：

1. `TryClearDungeon` 把参与标为已通关，图通关数 +1（封顶为所需次数），小队全体记入「有通关记录」（奖励资格）与成员通关数（`RaidManager.cs:890-944`）。
2. 全团：成员表（带各人通关数）。二阶段的 219 与满 5 次的 220 先发 `0x024B(id,3)`。
3. 全团：`0x0249(id, op=4)`。
4. 按图分派（下表），最后全团 `0x027B`。

| 图 | 上游动作 | 对应 PVF |
|---|---|---|
| 210 | 置 50=次数；满 4：210→3，再发 210→2、211→3、212–215→0；置 120=1、122=1；212/214 守图 300 秒；211 关闭 300 秒后开放并守 480 秒；20 秒后开放 216（`:212-238`、`:290-309`） | PVF:149-178 |
| 211 | 停 211 计时；1=0；211→3；300 秒后重开并守 480 秒（`:240-249`） | PVF:218-250 |
| 212 / 214 | 停守图；→3；150 秒后重开并守 300 秒（`:251-258`） | PVF:425-456 |
| 213 / 215 | 置 53/55=次数；满 2 → 3；两者都满 2 → 一阶段完成（`:260-273`） | PVF:499-524 |
| 216 | 停 216 计时；→3；124=1、2=0；150 秒后重开并守 360 秒，再开时 123=1 并重启陨石（`:275-288`） | PVF:293-322 |
| 218 | 立即 218→0、58=0、清通关数（可反复）（`:365-370`） | PVF:747-759 |
| 219 | 等 2 秒；停孵化开启与屏障计时；7=8=0；218/219→2；220→0；221–224 停计时并→2，3–6 与 9–12 清零（`:372-407`） | PVF:983-1006 |
| 220 | 置 59=次数；满 5：等 2 秒后二阶段完成（`:409-418`） | PVF:1007-1017 |
| 221–224 | 停计时；非感染源→3，感染源改发 `0x024A(id,3,感染)` 并置 7=0；清 FAIL/NAMED 符号；240 秒后重开（`:420-442`、`:701-732`） | PVF:775-830 |

### 3.4 机制计时到点

| 计时 | 到点动作 | 出处 |
|---|---|---|
| 211 守图 480 秒 / 212、214 守图 300 秒 | **一阶段失败复位**：置 104=1、102=1；再发复位符号组（含 104=0、102=0、121=1）；211–216→2；210→0；清全部一阶段通关数与计时。不踢人，在图里的小队照常打完 | `RaidHandler.Progression.cs:342-363`；PVF:251-264、457-474、525-559 |
| 216 守图 360 秒 | 125=1、102=1、53=55=0；212–215 清通关数并→0；重启 212/214/216 守图 | `:311-340`；PVF:333-360 |
| 211 / 216 周期 240 / 120 秒 | 置符号 1 或 2 = 1（每次地图开放只触发一次，到点后不再重排） | `RaidHandler.Runtime.cs:60-88`；PVF:205-217、371-383 中有 `RESET TIMER`，上游未重排是否正确 `Unknown` |
| 二阶段 219 类型 3，180 秒 | 置 128=1；4 个孵化中心随机去掉 1 个，开 3 个，其中随机 1 个为感染源；置 7=1、8=感染图号；`0x024A` 发 3 个 open 加感染图号；各开孵化中心启动类型 1（120 秒，之后每 45 秒）与类型 3（120 秒，之后每 50 秒） | `RaidHandler.Progression.cs:578-699`；PVF:760-774、831-982 |
| 孵化类型 1 / 3 | 置 3–6 / 9–12 = 1，按重复周期重排 | 同上 |
| 屏障回复 1 秒 | §2.4 | `:463-496` |
| 进攻超时（阶段时限，可被 buff 延长） | §3.7 | `RaidHandler.Runtime.cs:149-224` |

感染源只体现为符号 7/8 和 `0x024A` 尾部图号；感染如何影响屏障回复（34/秒）在上游实现，感染如何影响孵化中心战斗由客户端决定，`Unknown`。

### 3.5 阶段完成与切换

**一阶段完成**（`RaidHandler.Progression.cs:754-783`）：

1. `TryEnterPhaseBreak`：记下用时，State 3。停进攻计时与全部一阶段计时。
2. 全团：210–216 全部 `0x024B(→2)`；符号 1=2=0。
3. 冻结奖励资格（有通关记录的成员）。
4. 等 2 秒：置 105=1；进入结算：给每个在线成员单发 `RAID_RESULT`，全团 `RAID_STATE(3,0)`；再过 2 秒，若还没开始翻牌，全团发 `0x0258(0,0)`（`RaidHandler.Rewards.cs:302-338`）。
5. 交界：客户端 `0x0294 [01]` → 翻牌 → 奖励（奖励调研）。
6. 奖励完成 → `TryCompletePhase`：State 5；全团 `RAID_STATE(5,0)`、`0x0255(0,0,595)`、`0x0256(1,595)`；发 `ENABLE_CLEAR_DUNGEON` 让还在图里或已回城的人可以离开；排间歇计时（`RaidHandler.Rewards.cs:655-685`）。

**进入二阶段**（团长在 State 5 发空 body 的 `START_RAID`，或间歇 595 秒到点自动）：

1. 冻结准备名单，全团：攻坚对象、`0x0255(0,0,3)`、`0x0256(0,3)`、`PREPARE_START_RAID`（`RaidHandler.Progression.cs:54-95`、`:890-910`）。
2. 3 秒后 `CompletePhaseTwoPreparationAsync`（`:841-888`）：所有成员在线且实际小队就绪才继续，否则取消并发 `0x0256(1,0)` 与提示，留在 State 5。
3. 成功：`TryCompletePreparedNextPhase` 置 State 2 / Arg 1 / Phase 1，清空参与、通关数、奖励资格与成员通关数（`RaidManager.cs:1234-1263`）。
4. 全团：攻坚对象、`RAID_STATE(2,1)`；二阶段初始符号（57–63、3–6、9–12、7、8 为 0，106=1，110=10000，111=0）；`0x024A` 二阶段初始表（218/219 open，220–224 hide）；buff 与怪物表；127 先 0 后 1；`0x0255(0,0,2400)`、`0x0256(0,2400)`；排进攻超时；`0x0255(3,219,180)` 排孵化开启；启动屏障回复。

第三方：二阶段开打前再查门票，只对还没付过票的成员（如间歇期中途加入者）扣票，已付者带 `EntryCostPaid` 标记不重复扣（`T/…/RaidHandler.cs:3419-3427`、`:4266`、`:778-797`），且先发 `RAID_STATE(1,1)`、`(2,1)`。`ThirdPartyStatic`

**二阶段完成**（220 满 5 次，2 秒后，`RaidHandler.Progression.cs:444-461`）：State 3；停全部计时；218–224 全部→2；冻结资格；结算动画与翻牌同一阶段。奖励完成后 State 4，发 `ENABLE_CLEAR_DUNGEON`，清理运行态（`RaidHandler.Rewards.cs:665-672`）。攻坚对象**不解散**，成员停在 State 4 直到各自离团；之后如何收尾 `Unknown`。

### 3.6 PVF 与上游手写逻辑的差异 `ClientStatic(PVF)` vs `UpstreamStatic`

1. **KILL ALL CHARACTER**（PVF:342、534）：上游只置符号 102，不结束任何副本局。第三方在一阶段失败复位时把所有一阶段图里的局结束并送回城（`T/…/RaidHandler.cs:3248-3285`、`:3732-3766`），且把一阶段全部计时清零显示。
2. **周期计时**：PVF 的 211/216 周期效果到点后 `RESET TIMER`，是否循环取决于该引擎 `RESET TIMER` 的语义。上游只触发一次，孵化中心则按重复周期循环。
3. **`PARTY MAX`**：上游不限制同图小队数。第三方对孵化中心要求同图不能已有参与（`T/DfoServer.Game.Raid/RaidManager.cs:1613-1636`）。
4. **`EXCEPT CHEAT DUNGEON 220`**：两边都没实现。
5. **`RETRY`/`RETRY ITEM`**：两边都没实现；第三方另做了按库表配置的「60 分钟未通关退票」（`T/DfoServer.Game.Raid/RaidTicketRefundConfiguration.cs`），与 PVF 无关。

### 3.7 失败、超时、全灭、离图、掉线、重入

| 路径 | 上游行为 | 出处 |
|---|---|---|
| **进攻超时**（阶段时限到） | `TryFailAndDisband`：State 4/Arg 1，并**立即从管理器删除攻坚**；停计时；本阶段全部地图→2；一阶段置 104=1；全团 `0x0256(0,0)`；每人单发失败 `RAID_RESULT(1, phase, 用时, 死亡数, 0, 1)`；发 `ENABLE_CLEAR_DUNGEON`；清运行态并向频道广播攻坚移除与目录 | `RaidHandler.Runtime.cs:162-224`；`RaidHandler.Progression.cs:912-919`；自测 `U/SelfTests/A21RaidProtocolSelfTest.cs:1981-1986`（失败帧 25 字节） |
| 第三方超时 | 多发 `SET_SYMBOL(125,1)` 与一条文字公告 | `T/…/RaidHandler.cs:5937-5946` |
| **一阶段机制失守** | 复位而非失败（§3.4），总计时继续 | |
| **死亡** | 攻坚图内 `DIE_CHARACTER`：阶段死亡数 +1（决定评级），全团 `0x024E(user,0)` | `RaidHandler.Progression.cs:188-199`；`RaidManager.cs:628-645` |
| **复活币** | 小队参与的已用币数 +1，全团 `0x024E(user,1)` 与 `0x027B` | `:201-211`；`RaidManager.cs:647-676` |
| **全灭** | 无攻坚专属处理，按普通副本团灭流程；局结束后经退出回报离图 | — |
| **离图**（放弃、复活点返回、回城、加载超时） | 普通退出完成且本局已清空后：删除该小队的参与，全团 `0x0249(op=0)`、`0x027B`。已通关的小队也在离图时才删参与，之后可再次进同一张图 | `RaidHandler.cs:181-190`；`RaidManager.cs:946-978` |
| **主动离团** | `Leave`；**团长离团即解散**；成员离开不更新参与表里的小队名单 | `RaidHandler.cs:150-178`；`RaidManager.cs:1278-1319` |
| **掉线** | 会话清理即 `Leave`，同上；不保留 300 秒 | `RaidHandler.cs:515-527`；`RaidManager.cs:988-996` |
| **重入** `REJOIN_RAID` / 换会话重绑 | 重绑会话后单发：攻坚对象、`RAID_MODIFY` 信息、成员表、入场费状态、每个计时器按剩余秒数重算的 `0x0255`（与需要时的 `0x0256`）、State≠0 时补发通关数符号（一阶段 50/53/55，二阶段 59）、buff 表、怪物表。**不补发地图状态表、屏障与机制符号、参与快照** | `U/Network/Handlers/RaidHandler.Recruitment.cs:255-326`；`RaidHandler.Runtime.cs:419-473`；自测 `A21RaidProtocolSelfTest.cs:1740-1807` |
| 第三方重入 | 掉线保留 300 秒（成员带掉线截止时刻与代号，到点才离团）；记录所有发出的 `0x024A/024B/0248/0255` 的最新值，重入时补发地图状态、非动画符号与计时；掉线者若占着孵化中心且同队都已结束，释放该孵化中心参与 | `T/DfoServer.Game.Raid/RaidManager.cs:1040-1115`；`T/…/RaidHandler.cs:3057-3100`、`:5948-6009`、`:6618-6720` |

## 4. 上游领域模型

### 4.1 对象与职责 `UpstreamStatic`

| 对象 | 内容 | 出处 |
|---|---|---|
| `RaidAggregate` | 团号、实例 GUID、编成版本、团长版本、State/Arg/Phase、准备代号与冻结名单、阶段开始时刻、用时、延时秒数、死亡数、成员（含 `PartyIndex`）、成员阶段通关数 | `U/Game/Raid/RaidAggregate.cs:79-209` |
| `RaidManager` | 团目录与索引；**每团的参与表**（`RaidDungeonParticipation`：图号、小队名单、已通关、已用币、返还币）、**每图通关数**、**有通关记录者集合** | `RaidManager.cs:17-33`、`:1361-1368` |
| `RaidSituationGroup` | 按 `PartyIndex` 分组的只读视图；`PartyIndex==0` 的未编队成员每人单独成组；附带该组当前参与的图号与币数 | `RaidAggregate.cs:67-77`；`RaidManager.cs:793-889` |
| `RaidHandler` 进程字典 | 符号当前值、感染源、屏障是否已破、buff 激活与冷却、各组怪物运行值、阶段奖励流程、计时登记 | `RaidHandler.cs:37-55` |

地图状态**不在服务端保存**：上游只发不记，入场也不检查。第三方补了一份「最后发出值」（`T/DfoServer.Game.Raid/RaidManager.cs:1602-1611`），仅用于孵化中心入场与重入补发。

### 4.2 状态机

```
State0(招募) --START_RAID--> State0+StartPending --3s,扣票--> State2/Arg0/Phase0
State2/Phase0 --213&215满2--> State3 --奖励完成--> State5 --团长START或595s--> (准备3s) --> State2/Arg1/Phase1
State2/Phase1 --220满5--> State3 --奖励完成--> State4/Arg0（不解散）
State2(任一阶段) --进攻超时--> State4/Arg1 并删除攻坚
任意 --团长离团或掉线--> 删除攻坚
```

一阶段内部还有一个隐式子状态「黑雾是否已散」（符号 120），由 210 满 4 次进入，失守复位回到初始。

### 4.3 计时器实现

- 统一登记 `ScheduleRaidTimer(raid, type, dungeonId, purpose, seconds, 是否投影 SET_TIMER, REMAIN_TIME 种类, 回调)`（`RaidHandler.Runtime.cs:226-289`）。键为 `raid:{实例}:timer:{type}:{图}:{用途}`，同键重排会取消旧的。截止时刻存绝对 UTC。
- 回调执行前核对：同键登记仍是这一版本、实例与阶段未变、攻坚仍存在且 State/Phase 未变（`:275`、`:129-140`、`:321-340`）。重复计时用「保留版本」续排，避免被并发取消后复活（`:250-255`）。
- 取消：按 type+图（`:300-319`）、按阶段全部（`:352-370`）、按实例全部（`:372-412`）。
- 重入补发直接从登记表算剩余秒数（`:428-473`）。
- 底层是进程级 `ClockService` 一次性计时；不持久化，重启全丢。
- 另有若干裸 `Task.Delay`（读图后 300 ms、黑色火山与心脏通关后 2 秒、一阶段完成后 2 秒、动画提示 2 秒），之后用 `TryGetCurrentRaid` 复核。

### 4.4 与普通副本局的关系

- **每个小队一局普通副本**，攻坚完全不持有副本局，只通过上述钩子收到「已进图、已通关、死亡、复活、已离图」五种回报，再写自己的参与表。
- 副本局本身对攻坚只有三处改动：入场时攻坚频道要求 State 2；结算时取消翻牌；奖励策略关掉掉落和随机精英。
- 攻坚对副本局的反向操作只有发 `ENABLE_CLEAR_DUNGEON` 一种。上游从不结束、踢出或转移副本局。
- 第三方新增两种反向操作：一阶段复位时结束所有一阶段局送回城；能量阻截战局内直接「转移」到孵化中心（见 §5）。

### 4.5 并发

- `RaidManager` 一把全局锁，所有读写在锁内完成并返回快照。
- 符号、buff、屏障等在 handler 侧用 `ConcurrentDictionary` 与按实例的锁对象。
- 发包与 `await` 在锁外，靠快照的实例 GUID、阶段号与计时版本防旧回调。
- 第三方另加按团的 `SemaphoreSlim`，把一阶段的读图与通关回报串行化（`T/…/RaidHandler.cs:3223-3246`）。

## 5. 第三方商业端差异 `ThirdPartyStatic`

| 主题 | 第三方 | 上游 |
|---|---|---|
| 孵化中心入口 | **从能量阻截战 218 的四角房间直接转入**：客户端在 218 局内发 `SELECT_DUNGEON(221–224, 难度 2)`，要求所在房间是对应角落、存在「安徒恩的筋」机关、发起者是小队队长且全队都在同一房间、目标孵化中心状态为 open 且没有其他小队；新建孵化中心局替换 218 局，参与记录从 218 改为目标图并继承币数 | 孵化中心按普通选图进入，没有特殊处理 |
| 时长 | 一阶段 3120、守图 390、黑雾 624、舰炮 468（均为 PVF 值 ×1.3），二阶段 2400；不读 PVF | 读 PVF |
| 开团人数 | 检查 PVF 最低人数 | 不检查 |
| 二阶段 | 对未付票成员补扣；先发 `RAID_STATE(1,1)` 再 `(2,1)` | 不检查、不扣票；只发 `(2,1)` |
| 掉线 | 保留 300 秒，可重入；孵化中心占位释放 | 立即离团，团长掉线即解散 |
| 重入补发 | 地图状态、非动画符号、计时都补 | 只补计时、通关数、buff、怪物 |
| 一阶段复位 | 结束一阶段所有局送回城，计时显示清零 | 只发符号与地图状态 |
| 计时停止显示 | 停计时时发 `SET_TIMER(type,id,0,0)` | 不发 |
| 死亡与复活 | 不发 `0x024E` | 发 |
| buff 延时 | 不设剩余时间上限 | 剩余时间不超过阶段时限 |
| 失败反馈 | 额外 `SET_SYMBOL(125,1)` 与文字公告 | 只发结果帧 |
| 失败退票 | 60 分钟未通关自动退票（库表配置） | 无 |

`T/` 出处：孵化中心转移 `DfoServer.Game.Dungeon/AntonHatcheryEntranceRules.cs:16-40`、`DfoServer.Network.Handlers.Dungeon/DungeonEntryHandler.cs:417-432`、`:1126-1136`、`:3303-3390`、`DfoServer.Game.Raid/RaidManager.cs:1613-1658`、`DfoServer.Network.Handlers/RaidHandler.cs:3102-3112`；时长 `RaidHandler.cs:3639-3641`、`:3690`、`:4236`；其余见 §3 各处引用。

孵化中心入口的旁证 `ClientStatic(PVF)`：218 的四角对应的被动对象 18862/18863/18892/18893 都叫「安徒恩的筋」，动作文件分别为 `wall_Basic_224/221/222/223.act`（`passiveobject/ActionObject/Map/Anton/Phase3/wall/*.obj`），与第三方的角落到孵化中心对应一致（`(0,0)→224、(3,0)→221、(3,4)→222、(0,4)→223`）。这说明孵化中心的入口很可能在 218 地图内部，客户端是否真的从 218 局内发选图，`Unknown`。

## 6. 对 JP86 设计的影响

### 6.1 复用普通 `DungeonRun` 的可行性

可行，而且上游、第三方都是这样做的：一个小队一局普通副本，攻坚只消费局的结果。这与 `Document/AntonRaidDesign.md` 的三层成员和「每支出战小队复用普通 `DungeonRun`」一致。需要补的是下面这些接缝，其中 6.3 的两点会碰到现有约束。

### 6.2 需要的钩子

| 需要 | JP86 现有机制 | 做法建议 |
|---|---|---|
| 玩法识别 | `DungeonRunKind` + `DungeonRunMode`（`J/Document/DungeonRunKinds.md:34-42`） | 新增 `AntonRaid` 种类；`Mode` 冻结攻坚实例身份、阶段号、准备代号、`PartyKey`。识别一律看 `Mode`，不按图号段（上游 216 漏判就是按图号段的后果） |
| 入场 | `IDungeonEntryRoutingDomainService`（`DungeonRunKinds.md:62-67`） | 攻坚频道里登记在最前：由攻坚实例裁决阶段、地图是否 open、同图小队上限（PVF `PARTY MAX`）、小队有且只有一名队长、普通图一律拒绝。上游这几项都没查，JP86 应补齐 |
| 能力表 | `DungeonRunCapabilities`（`J/Server/src/Dfo.Server.Abstractions/World/DungeonRunCapabilities.cs`） | `DrawsClearCards=false`（上游取消翻牌）、`UnlocksDifficulty=false`、`AllowsOrdinaryRechallenge=false`；`Revival` 需要新增「攻坚币额度」类策略（`INCREASE COIN` 与 `0x027B` 的已用币数）；`AppliesFailureWeakness`、`PartyWipe` 的取值 `Unknown`，先按普通处理并列入实机项 |
| 奖励策略 | 掉落、任务掉落、随机精英 | 照上游：保留经验与任务进度，关闭怪物掉落、任务掉落、随机精英 |
| 回报：读图完成 | 加载就绪（地下城运行服务） | 攻坚登记参与时用**实际进图名单**，不要用编成名单（上游会把没进图的队友算成参与和通关） |
| 回报：通关 | `DungeonMapCleared`（`IsBossRoom`）或 `IDungeonBossClearHook` | 首领房清场时在同一事务里给攻坚记一次通关，避免上游那种依赖 `0x002E` 结算时机、靠「已通关」标志防重的做法 |
| 回报：死亡、复活 | 战斗与复活领域服务 | 死亡数决定评级；复活要扣攻坚的小队币额度 |
| 回报：离图 | `DungeonParticipantLeft`（`J/Server/src/Dfo.Server.Abstractions/World/DungeonLifecycleEvents.cs`） | 全队离图时释放该图的小队占用 |
| 攻坚对局的反向操作 | 按 `RunId` 结束整局的入口（设计草案已提） | 一阶段复位（PVF 的 `KILL ALL CHARACTER`）、阶段结束、超时失败、团收尾都要能结束局或至少放开回城；最终语义待实机 |
| 到点推进 | `DungeonDeadlineScheduler` + `DungeonDeadlineKind`（`DungeonRunKinds.md:69-84`；`J/Server/src/Dfo.Server.Abstractions/World/DungeonDeadlines.cs:4`） | 新增一个攻坚来源：截止时间存在攻坚实例里（阶段时限、各图守图/重开/周期、延时开放、孵化开启、间歇、准备 3 秒、动画提示 2 秒），由实例提交时唤醒；回调靠实例身份 + 阶段 + 代号丢弃过期项。上游的裸 `Task.Delay` 都应改成截止时间 |
| 唯一执行器 | `Document/GameplayExecution.md` | 上游的全局锁、按团锁、信号量都不需要；所有攻坚命令与副本回报在执行器内串行 |

**建议把 PVF 触发器脚本做成数据驱动。** §2.3 的词汇封闭且很小，整份 `anton.etc` 都能用「条件 → 动作」解释器表达，计时、地图状态、符号都是它的状态。上游手写的分支在几处与脚本不一致（§3.6），且第三方又改了一版。用解释器能让 PVF 成为唯一事实，手写的只剩脚本之外的规则：通关计数阈值（`HP INFO`）、孵化随机与感染、屏障回复、阶段完成后的结算。这是设计建议，不是上游事实。

### 6.3 冲突点

1. **孵化中心的局内转移**。若 A21 确实从 218 局内直接进孵化中心（第三方做法与 PVF 旁证），这是「一局换到另一局、不回城」。JP86 现有 `DungeonRun` 没有这种转移，需要新的入场路径：在 218 局内以小队为单位结束旧局、开新局，并继承币数。上游没有这条路径，无法对照。要最先实机确认。
2. **攻坚计时不属于任何一局**。`DungeonDeadlineScheduler` 现在的来源都是「截止时间随局出现和消失」。攻坚计时跨越多局，甚至在没人出战时也在走。调度器的拉取式接口本身能承接，但要在文档里写明这是唯一一个状态所有者不是局的来源，或者另起一个同构的攻坚调度来源，不能塞进局的 `ModeState`。
3. **掉线与重入**。上游团长掉线即解散；PVF 有 300 秒掉线等待，设计草案也保留团员身份。按 PVF 与第三方：团员身份保留到期满，局照常因掉线结束。重入时要补发完整态势（地图状态表、全部非动画符号、计时、参与快照），上游缺前两项。
4. **阶段间的编成冻结**。上游二阶段开始时清空参与与通关记录、重算资格；改编只在局外（`U/Network/Handlers/PartyHandler.cs:97`）。与草案「涉及选图或出战的小队拒绝改编」一致。
5. **地图状态由服务端持有**。上游不存地图状态，只发包；JP86 入场裁决需要它，所以必须由攻坚实例持有，并在重入时整表补发。
6. **屏障每秒广播**。上游回复期间每秒向 20 人广播一次 `0x0248`。JP86 可以照做，也可以只在破/满时发并让客户端插值，后者需要实机确认客户端是否自行回复。

## 7. 未知与待实机项

按开工前的阻断程度排序。

1. **地图状态切换**：客户端各 UI 在何时展示 `close=1` 和 `clear=3`，用真实切换序列验证（§2.5）。
2. **孵化中心入口**：A21 战况导航排除 221–224，dev PVF 的 [218 四角小队传送](04-jp86-protocol-client-pvf.md#218-到孵化中心的传送) 明确目标图。留待实机确认切图和加载顺序、队伍及源局衔接。
3. **`0x0296`/`0x0247` 语义**：两者已确认读写 6 字节；字段的业务含义和发出场景待实机。
4. **`0x0297` 的来源**：哪个场景、对哪些符号发；是否只有 110；客户端是否自己执行 `anton.etc` 的部分触发器再上报。这决定服务端解释器与客户端谁是权威。
5. **额外符号**：上游使用的 111 在 dev PVF 未定位，检查其作用与显示。
6. **无 CMD 响应 handler 的 ACK**：`0x0296/0297/02B7/02B8` 回 ACK 是否有害或被忽略。
7. **`0x024A` 投影**：感染图号位于包尾，实机确认感染出现和清除时的帧序。
8. **`0x024E` 提示**：0 才进入成员文本路径，确认死亡时的文字与是否需要其他通知。
9. **`0x0249` op 语义**：0/1/2/4 各对应什么显示。
10. **`0x02B8` 值的含义**：Boss/精英血量还是别的；哪些路径发、`n` 取值。
11. **`KILL ALL CHARACTER`**：客户端自行处死图内角色，还是需要服务端结束局。
12. **周期计时**：211 黑雾、216 陨石在 PVF 里 `RESET TIMER` 后是否循环。
13. **`PARTY MAX`** 的确切含义（同图小队上限？）与超限时客户端表现。
14. **屏障**：扣减由谁发、回复周期是否 1 秒、感染期间 34 是否正确、客户端是否自行显示回复。
15. **`0x0255` 计时补发**：已知两个 u32 为 Unix 秒截止及持续秒数；验证重入所需的计时集合和值。
16. **`0x0258`、`0x0273`、`0x028A`** 的布局与发送时机（静态读不出）。
17. **二阶段开打**：是否需要先发 `RAID_STATE(1,1)`；间歇期中途加入者的门票怎么处理。
18. **失败后**：`RETRY`/`RETRY ITEM 10099897` 的发放与重试资格；超时后在图内的局如何结束；攻坚对象解散的帧序。
19. **二阶段完成后**：攻坚停在 State 4 的时长与解散方式。
20. **全灭**：攻坚图内团灭是否有专属结果（`AppliesFailureWeakness`、`PartyWipe` 取值）。
21. **时长**：第三方 ×1.3 是其业务配置还是另有依据；A21 以 PVF 为准的前提需实机计时核对。

建议首轮实机取证顺序：210 进图→读图→通关→回城的完整双向帧与服务端逐帧日志（定 `0x0249/024B/0248/027B` 与状态字节）；然后黑雾散开的整串推进；最后二阶段 218 到孵化中心的入口。
