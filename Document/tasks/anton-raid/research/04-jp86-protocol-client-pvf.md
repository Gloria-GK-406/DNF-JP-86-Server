# 04 安图恩攻坚：JP86 协议页、客户端证据与 PVF 盘点

2026-11-19，只读调研。范围是 A21 安图恩团队模式（攻坚，`Etc/Raid/Anton.etc`）。安徒恩普通、觉醒、任务版和双人 Raid（dual raid）只在 §4.12 列出边界，不展开。上游业务逻辑不在本篇范围，这里只记录协议页里已经抄进来的上游含义，以及这些含义和客户端读宽之间的冲突。

## 0. 绑定版本与标签

| 项 | 值 |
|---|---|
| 客户端 | A21 `DNF.exe` SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。协议页写 **RVA**，ClientHack 文档写 **VA**，两者差 `0x400000`（例：NOTI `0x0247` handler RVA `0x00D50FD0` = VA `0x01150FD0`；共享空桩 RVA `0x01E31720` = VA `0x02231720`） |
| PVF | `ClientStore/mounts/dev/content/Script.pvf`，`content:0.3.3-dev.1`，SHA-256 `a97132dda0970b3e0ce7c3de8052acf0be2664f52e26c819b1d8cb6d427cfcd2`（本次实测） |
| A21 分析镜像 | `ClientEvidence/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`，SHA-256 `aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d`，基址 `0x00400000`（见同目录 `manifest.json`） |
| A21 全 RVA 分析 PE | `ClientEvidence/binaries/DNF.unpacked-b1-full-e5a34bbf.exe`，SHA-256 `e5a34bbfcf5f7da99a71cbe6c4a7d10dd8ed700c68059f2c7ddafa3f141c0ee8`，基址 `0x00400000` |
| 服务端 | worktree `DNF-JP-86-Server-anton-raid` |

[protocol/evidence.md](../../../../protocol/evidence.md) 定义的是证据**种类与效力顺序**，没有给出下列标签名。本文沿用仓内 `patterns/flow-*.md` 已经在用的写法，并对应到 evidence.md 的种类：

| 标签 | 含义 | 对应 evidence.md 种类与上限 |
|---|---|---|
| `ClientStatic` | 协议页机器区静态走图、`protocol-identity.tsv` 注册表 | 4 级静态走图，S→C 只是下界 |
| `ClientStatic(hand)` | DNF-JP-86-ClientHack `Docs/a21-protocol/proto-*.md` 的逐函数手工反汇编，以及 `ClientHack/tools/literals-a21.csv` 的解密字面量 | 仍是静态，但读到了 read 原语的调用顺序和计数字段 |
| `ClientStatic(proximity)` | 只凭构包点或 handler 与带字面量函数的地址相邻推断归属 | 线索，最高 Hypothesis |
| `UpstreamReference` | 协议页 `可能含义` 列中引自 ServerS4A21 的含义（来自 `patterns/data/meanings/*.tsv`） | 5 级上游转录，**最高 Hypothesis**。页面里标的 `Confirmed` 超出了 evidence.md 允许的上限 |
| `CurrentImplementation` | 本仓当前代码 | 只说明代码现状 |
| `PvfContent` | dev PVF 实测内容 | 数据事实，不证明日服 A21 入口开放 |
| `Unknown` | 无证据或未读到 | — |

## 1. JP86 协议页盘点

### 1.1 总体结论

- 生成区是静态普查。各协议新增的 A21 指令证据见对应协议页人工区。暂无团本线级样本或实机记录。
- 已有静态证据按 [协议清单](../../../../protocol/catalog.md) 查阅，端到端时序仍在 [服务设计的取证项](../design.md#82-a21-实机取证)。
- `GameProtocol.cs` 和 `NetworkMessages.cs` 没有任何 raid opcode 常量或消息成员。注意编号同号不同协议：`GameProtocol.cs:124` 的 `SecurityStatusType = 0x0252` 是 C→S `SECURITY_STATUS`，`:151` 的 `BossTowerMapType = 0x0334` 是 NOTI，`:286` 的 `0x02EF` 是 C→S FPS 统计。它们与本表的 NOTI `0x0252`、C→S `0x0334`、NOTI `0x02EF` 都无关（`CurrentImplementation`）。
- 协议目录没有单独的 raid CMD 响应页（`server-to-client/` 下只有 `CMD-0x02B6.md`、`CMD-0x036F.md`）。响应读法见 §1.3，来自 ClientHack `proto-5.md` 与 `proto-3.md`。
- 任务清单里的 NOTI `0x0254` **没有协议页**，A21 NOTI 注册表也没有登记 `0x0254`（`protocol-identity.tsv` 的 NOTI 序列从 `0x0253` 跳到 `0x0255`）。字面量表里有 `ENUM_NOTIPACKET_START_RAID`，它在字面量区里排在 `RAID_SET_TIMER`（`0x0255`）与 `PREPARE_START_RAID`（`0x0253`）之间，所以推断它就是 NOTI `0x0254`。即使如此，客户端也**没有为它注册 handler**（`ClientStatic(hand)` / Hypothesis）。

### 1.2 C→S 请求（`protocol/game/client-to-server/`）

「响应」列记录 A21 是否注册同号 CMD handler。回包是否参与客户端状态推进应按 handler 分支及实机确认，空桩和带字段的 handler 分开看。

| Type | 名称 | 布局 | BodyLength / 结构（`ClientStatic`） | 字段摘要（页面「可能含义」，来源标签） | 构包点 RVA | 响应 handler RVA |
|---|---|---|---|---|---|---|
| `0x0293` | RAID_ENTRY_COST_INFO | 闭合 | 1 | `+0 u8` stage；上游：stage==0 时额外下发 buff/怪物状态（`UpstreamReference`） | `0x0082CD39`, `0x00B986D6` | `0x01E31720` = **空桩 `ret`** |
| `0x0294` | RAID_MOVIE_SKIP | 闭合 | 1 | `+0 u8` 过场完成或跳过标志；上游 ==1 时开一阶段翻牌（`UpstreamReference`） | `0x006D0693`, `0x006D06D7` | `0x01E31720` 空桩 |
| `0x0295` | SELECT_RAID_REWARD_CARD | 闭合 | 2 | `+0 u8` rewardType（上游：1=party_card，否则 gold）；`+1 u8` cardIndex ≤3（`UpstreamReference`） | `0x006DFDF5`, `0x006DFE2B`, `0x006DFE70` | `0x00D144D0` |
| `0x0296` | RAID_DO_BEHAVIOR | 闭合 | `u32 + u16` 共 6B | [A21 构包指令](../../../../protocol/game/client-to-server/0x0296.md) 确认宽度，上游的 8B 长度校验不兼容 | `0x0227C145` | 无 |
| `0x0297` | RAID_SET_SYMBOL | 闭合 | 9 | `+0 u32` symbolId（上游只收 110=黑色火山屏障）、`+4 u32` operand、`+8 u8` operation 0/1/2=赋值/加/减（`UpstreamReference`）。symbolId 与 PVF `anton.etc.symbol` 编号一致（§4.6） | `0x00822E8A` | 无 |
| `0x0298` | RAID_MESSAGE | 部分 | `u8` + `dstring`（`i32 长度+字节`，见 game-frame `WriteDString`）；构包经虚表，载荷随运行时类型变 | 全部 unknown | `0x0228BD51` | 无 |
| `0x0299` | CREATE_RAID | 部分 | `u8` + `dstring` | 页面 unknown；meanings tsv 有上游「offset 4 = i32 长度前缀标题」，**与客户端 `+1` 起的 dstring 错位 3 字节**，未进入机器表（`UpstreamReference`） | `0x00B91EF7` | `0x00D13AC0` |
| `0x029A` | LEAVE_RAID | 闭合 | 2 | `+0 u16` unknown（取自 `[esi+0x24]`） | `0x00823302`, `0x0219401A` | `0x00D14150` |
| `0x029B` | START_RAID | 闭合 | **0**（构包器只写 header） | — | `0x00823BE1` | `0x00D13D90` |
| `0x029C` | SET_RAID_WAITING | 闭合 | 2 | `u8 u8` unknown | `0x00B97F94` | `0x00D143B0` |
| `0x029D` | REJOIN_RAID | 闭合 | 4 | `+0 u32` unknown | `0x00D6FF18` | `0x00D13F90` |
| `0x029E` | RAID_MANAGER_WORK | 闭合 | 12 | `+0 u32` op（上游只收 0=分配到小队）、`+4 u32` targetActorId（上游只读低 u16）、`+8 u32` partyIndex（0=未编队）（`UpstreamReference`） | `0x00B91096`, `0x00B98DC1`, `0x021966EE` | `0x01E31720` 空桩 |
| `0x029F` | MODIFY_RAID_INFO | 部分 | `u8` + `dstring` | 同 `0x0299`，上游 offset 4 标题与客户端错位 | `0x00B92007` | 无 |
| `0x02B7` | RAID_BUFF_SYSTEM | 闭合 | `u8, u16, n×u16`，无独立目标条数 | `+0 u8` buffType（上游 0..4=INCREASE TIME/ATTACK BONUS/INVINCIBLE/RESTORE/INCREASE COIN）、`+1 u16` partyIndex、`n×u16` 目标 userId（`UpstreamReference`） | `0x0066DB63` | 无 |
| `0x02B8` | RAID_MONSTER_HP | 闭合 | `u8 n + n×u32`，三个构包点同布局（含 `n=0`） | [A21 构包指令](../../../../protocol/game/client-to-server/0x02B8.md) 确认宽度；限值 5 及运行值含义仍为参照 | `0x006501AB`, `0x0065028C`, `0x006505E2` | 无 |
| `0x0331` | RAID_OTHER_CHANNEL_USERINFO | 闭合 | 4：`u8 u16 u8` | 全部 unknown | `0x00671C89`, `0x01FCCE19`, `0x021361BE`, `0x021958D0`, `0x02341A7E` | 无 |
| `0x0332` | RAID_OTHER_CHANNEL_REQUEST_JOIN | 部分 | ≥9：`u8 u16 u32 u16`，构包经虚表 | 全部 unknown | `0x00822DB3` | `0x00D1F1F0` |
| `0x0334` | RAID_RECENT_FRIEND_LIST | 结构未知 | 构包器未调用写原语，也没有正常收尾 | — | `0x00B9B1FE` | `0x00B9FB20`（forwarder 注册） |
| `0x0363` | RAID_REQUEST_RAID_MEMBERS | 闭合 | 4：`u32` | unknown | `0x008238A2` | 无 |

### 1.3 CMD 响应（S→C，同号 CMD）

帧级信封：每个 CMD 响应 body 先是 `u8 bSuccess`，`bSuccess==0` 时再跟 `u8 errorCode`，之后才是 handler 自己的读取（ClientHack `Docs/a21-protocol/proto-5.md` §2.4，`ClientStatic(hand)`）。

| Type | handler VA | 成功路径读取（`ClientStatic(hand)`） | 出处 |
|---|---|---|---|
| `0x0293` / `0x0294` / `0x029E` | `0x02231720` | **单条 `ret`**。客户端收下后丢弃，回包不会产生任何客户端效果 | proto-5.md:234–242、:626–628 |
| `0x0295` | `0x011144D0` | 只看信封，零读取 | proto-5.md:280–284 |
| `0x0299` | `0x01113AC0` | `u32` | proto-5.md:310 |
| `0x029A` | `0x01114150` | `u8` | proto-5.md:311 |
| `0x029B` | `0x01113D90` | 按 errorCode 分支：`0x11` → `u32 n` + `n×u32`；`0xD9` → `u32 n` + `n×u32`；其他错误码无载荷 | proto-5.md:363–369 |
| `0x029C` | `0x011143B0` | 只看信封，零读取 | proto-5.md:280–284 |
| `0x029D` | `0x01113F90` | 只看信封，无额外读取 | [响应路径](../../../../protocol/game/client-to-server/0x029D.md) |
| `0x0364` | `0x0110DD50` | 成功多读一个用于后续查团的 `u32` | [响应路径](../../../../protocol/game/client-to-server/0x0364.md) |
| `0x0332` | `0x0111F1F0`（与 `0x0333` 共用） | 只看信封，零读取 | proto-3.md:484–509 |
| `0x0334` | `0x00F9FB20`（RVA `0x00B9FB20`） | 未分析 | `Unknown` |

### 1.4 S→C NOTI（`protocol/game/server-to-client/NOTI-*.md`）

「页面下界」是机器区静态走图。「手工布局」来自 ClientHack 文档：`proto-0.md`（`GameCoreNetwork_NOTI_Dungeon.cpp`，行 225–241）、`proto-1.md`（`GameCoreNetwork_NOTI_Etc.cpp`，行 368–411）、`proto-2.md`（`NOTI_Town.cpp`，行 397–447）、`proto-6.md`（`CNRDUserInfoPacketHandler.cpp`）。`N(u8)×{…}` 表示以前面读出的 u8 为计数的重复组。proto-1 明确说明：只给宽度和顺序，不给语义；标 `[SWITCH]` 的行不可直接当线性结构用。

| Type | 名称 | 页面状态 / 下界 | 手工布局（`ClientStatic(hand)`） | 页面已挂的上游含义（`UpstreamReference`） | handler RVA |
|---|---|---|---|---|---|
| `0x0247` | RAID_DO_BEHAVIOR | 部分，≥6 | `u32 u16`（6B），与 C→S `0x0296` 的 `u32+u16` 同形 | — | `0x00D50FD0` |
| `0x0248` | RAID_SET_SYMBOL | 部分，≥1+8n | `u8 n + n×(u32 ID, u32 值)` | `(0,0)` 清空并提前结束；见 [符号通知](../../../../protocol/game/server-to-client/NOTI-0x0248.md) | `0x00D48040` |
| `0x0249` | RAID_DUNGEON_PARTICIPATION_INFO | 部分，≥9 | `u8 N(u8)×{u32 u8 u8 N(u8)×{u16}}` | 旧机器区转录按 `u32` 对齐；当前上游与客户端都用窄字段，首项是 A21 外层分组计数。见 [参与通知](../../../../protocol/game/server-to-client/NOTI-0x0249.md) | `0x00D515B0` |
| `0x024A` | RAID_DUNGEON_STATE | 部分，≥6+5n | `u8 u8 N(u8)×{u32 u8} u32`：**尾部还有一个 u32**，页面误放到了 offset 2 | 旧转录偏移错误。首字节读后未用，感染图号位于列表后，见 [地图表](../../../../protocol/game/server-to-client/NOTI-0x024A.md) | `0x00D480B0` |
| `0x024B` | RAID_CHANGE_DUNGEON_STATE | 部分，≥6 | `u32 u8 u8`（6B） | 旧转录的 `+8` 超出布局。状态在 `+5`，枚举见 [地图状态](../../../../protocol/game/server-to-client/NOTI-0x024B.md) | `0x00D49520` |
| `0x024C` | RAID_STATE | 部分，≥2 | `u8 u8`（2B） | 旧转录的 `+4` 超出布局。A21 第二个字节在状态 2 区分两阶段；见 [阶段状态](../../../../protocol/game/server-to-client/NOTI-0x024C.md) | `0x00D5E9A0` |
| `0x024D` | RAID_MESSAGE | 部分，≥1 | `u8` | — | `0x00D48180` |
| `0x024E` | RAID_MEMBER_STATE | 部分，≥3 | `u16 u8`（3B） | 旧转录给出的 1/2 和 A21 路径不符。A21 仅值 0 进入成员文本处理，见 [成员提示](../../../../protocol/game/server-to-client/NOTI-0x024E.md) | `0x00D48200` |
| `0x024F` | RAID_LIST | 部分，≥4+25n | `u32 LOOP{u8}`（proto-1 的压缩写法，比页面粗） | — | `0x00D834B0` |
| `0x0250` | RAID_MODIFY | 部分，≥8 | `[SWITCH] 2*u32, u8` | — | `0x00D84BD0` |
| `0x0251` | RAID_WAITING_LIST | 部分，≥5 | `u16 LOOP{u16, u8}` | — | `0x00D64860` |
| `0x0252` | RAID_WAITING_MODIFY | 部分，≥4 | `u8, u16, u8`（4B） | — | `0x00D64A60` |
| `0x0253` | PREPARE_START_RAID | 结构未知（打包区） | **无 read 原语调用**，疑为零长 body | — | `0x00D734B0` |
| `0x0254` | （START_RAID，推断） | **无页面、无注册** | — | — | — |
| `0x0255` | RAID_SET_TIMER | 部分，≥13 | `u8 u32 u32 u32`（13B）；同一函数注册两次（无害重复） | 旧转录偏移错误。当前上游已用同一窄字段布局；A21 在 `+5/+9` 使用 Unix 秒截止及持续秒数，见 [计时通知](../../../../protocol/game/server-to-client/NOTI-0x0255.md) | `0x00D4EFE0` |
| `0x0256` | RAID_REMAIN_TIME | 部分，≥5 | `u8 u32`（5B） | `+0` 计时器类型 0/1、`+1` 剩余秒（与客户端一致） | `0x00D48140` |
| `0x0257` | RAID_ENTRY_COST_INFO | 部分，≥4+5n | `u32 LOOP{u16, u8, u16}` | 生成区的旧上游偏移与 A21 不同。当前上游编码为相同的 5B 条目；见 [读取点](../../../../protocol/game/server-to-client/NOTI-0x0257.md) | `0x00D602E0` |
| `0x0258` | RAID_MOVIE_SKIP | 结构未知（打包区） | 无 read 原语调用 | — | `0x00D6C2C0` |
| `0x0259` | RAID_REWARD_LIST | 部分，≥12 | `2*u8 LOOP{u16, 2*u8, u32, u16}` | 旧转录把条目数放到 `+4`；A21 计数实际位于 `+1`。当前上游也用两个 `u8`，见 [奖励列表](../../../../protocol/game/server-to-client/NOTI-0x0259.md) | `0x00D86EC0` |
| `0x025A` | RAID_RESULT | 部分，≥10 | `2*u8, u32, u16, 2*u8`（10B，非 SWITCH） | 旧转录使用六个 `u32`。当前上游与 A21 都用 10B，结果分支见 [阶段结果](../../../../protocol/game/server-to-client/NOTI-0x025A.md) | `0x00D764F0` |
| `0x0273` | CLEAR_RAID_BUFF | 结构未知 | 无直接读取，解析委托给 VA `0x00442570` | — | `0x00D68C70` |
| `0x027A` | RAID_BUFF_SYSTEM | 部分，≥15 | `u8 N(u8)×{u8 u8 N(u8)×{u16 u16 u32 u32}}` | 组数；{buffType, 条目数, {partyIndex, userId, activeUntil, cooldownUntil}}（与客户端形状吻合） | `0x00D5EEA0` |
| `0x027B` | RAID_MONSTER_HP | 部分，≥11+4n | `u8 N(u8)×{u16 u8 N(u8)×{u16} u32 u8 N(u8)×{u32}}` | {situationIndex, 成员数, 成员 userId[], usedCoin, 运行值数, 运行值[]}（形状吻合） | `0x00D5F1D0` |
| `0x028A` | RAID_INOUT_SYSTEM | 部分，≥4 | `u32` | — | `0x00D76C50` |
| `0x02EA` | RAID_OTHER_CHANNEL_WAITING_MODIFY | 部分，≥40 | `[SWITCH] 2*u8, u16` | — | `0x00D6A9D0` |
| `0x02EB` | RAID_OTHER_CHANNEL_WAITING_LIST | 部分，≥43 | `u32 LOOP{u16, 2*u8, u16, 2*u8, bytes[1], bytes[30]}` = 4+39n | — | `0x00D6AD70` |
| `0x02EC` | RAID_OTHER_CHANNEL_LIST | 部分，≥4+25n | `u32 LOOP{u8}` | — | `0x00D835F0` |
| `0x02ED` | RAID_OTHER_CHANNEL_USERINFO | 部分，≥52 | `u8 flag`、`u16`（读后丢弃）、一份 `readOtherUserInfoMinimum` 加一份 `OtherCharacter`。**只能发一组**，发多组会让客户端失步；userId 未知时中途中止（proto-6.md §3.2、§7） | — | `0x00FE9830`（forwarder） |
| `0x02EE` | RAID_OTHER_CHANNEL_REQUEST_JOIN | 部分，≥50 | `bytes[50]` | — | `0x00D91870` |
| `0x02EF` | RAID_OTHER_CHANNEL_RESPONSE_JOIN | 部分，≥5 | `bytes[5]` | — | `0x00D8C2A0` |
| `0x02F0` | RAID_RECENT_FRIEND_LOGIN_INFO | 部分，≥5 | proto 文档没有覆盖（forwarder 注册） | — | `0x00B9FD70` |
| `0x0314` | ALEADY_RAID_REWARD | 结构未知 | handler 为 `0x02231720` **裸 `ret`**，登记了但没有实现（proto-1.md:403） | — | `0x01E31720` |
| `0x0327` | RAID_MEMBERS（页面标题写成 `OBJECT_DESTROY_REWARD`） | 部分，≥5 | `u32` | — | `0x00D85510` |
| `0x0328` | RAID_DISCONN_INFO | 部分，≥6 | `u32, u16`（6B） | — | `0x00D6FE00` |
| `0x04BD` | RAID_USER_FINISH_LOAD | 结构未知 | 零长 body（proto-2.md:416、:447） | — | `0x00D8C0B0` |

### 1.5 协议页层面的风险

| 问题 | 影响 | 标签 |
|---|---|---|
| `patterns/data/meanings/09-raidhandler-progression.tsv`、`12-protocol-gameprotocolhandler.tsv` 等把上游含义标为 `Confirmed`/`High`，但来源全是 ServerS4A21 | 按 evidence.md 上游最高只到 Hypothesis。照页面实现会把上游当事实 | `UpstreamReference` |
| 旧上游转录大量按 **u32** 对齐字段（`0x0249`/`0x024A`/`0x024B`/`0x024C`/`0x0255`/`0x0257`/`0x0259`/`0x025A`），A21 客户端读的是 u8/u16 | 生成器按 offset 把上游含义挂到客户端字段上，**挂错了位**。实现应以协议人工区的 A21 布局为准；当前上游部分 builder 已改用窄字段 | `ClientStatic(hand)` vs `UpstreamReference` |
| C→S `0x0299`/`0x029F` 的旧上游转录把 dstring 起点写成 offset 4，A21 在 offset 0 写 `u8 0`，从 offset 1 写 dstring | 解包器按客户端读 | 同上 |
| C→S `0x0296` 上游要求 8B，A21 已静态确认 6B | 实现按 6B，场景和语义待实机 | 同上 |
| NOTI `0x0327` 页面标题与身份行名称不一致 | 只是页面标题问题，身份表与字面量都是 `RAID_MEMBERS` | `ClientStatic` |
| 相邻编号的名称冲突：C→S `0x0333/0x0335/0x0336/0x033C` 和 NOTI `0x02F1–0x02F6` 在 `game/index.md` 里是 RAID_* 名，页面标题却是 ADVANCE_ALTAR 或活动名。proto-2 把 NOTI `0x02F2` 记为 `RAID_OTHER_CHANNEL_USER_TITLE_BOOK`（`u8 u16 u8 u8 u8_count×{…}`） | 这些协议不在本次清单里。若跨频道组队需要用到，要先裁定名称 | `ClientStatic` |
| 字面量表另有 `ENUM_NOTIPACKET_RAID_PHASE_COIN_INFO`、`ANTON_CONQUEST_CLEAR_COUNT`、`ALEADY_RAID_REWARD`，以及 `UDPPACKET_ANTON_REWARD_SELECT_CARD` | 前两者在 A21 NOTI 注册表里未定位到 type。UDP 名称是进一步取证线索，是否实际参与翻牌同步待证 | `ClientStatic(hand)`（`literals-a21.csv`） |

## 2. 客户端侧证据

| 路径 | 结论 | 标签 |
|---|---|---|
| `DNF-JP-86-ClientHack/Docs/a21-protocol/proto-0.md:225–241,317` | NOTI_Dungeon 内 raid NOTI 的手工读序（`0x0247–0x024E`、`0x0255`、`0x0256`、`0x027A`、`0x027B`）；`0x0255` 被注册两次；`0x02231720` 是退役 id 共用的空桩 | `ClientStatic(hand)` |
| `.../proto-1.md:368–411,180,403,450–454` | NOTI_Etc 内 raid NOTI（`0x024F–0x025A`、`0x0273`、`0x028A`、`0x02EA–0x02EC`、`0x0327`、`0x0328`）的读序；`0x0314` 是裸 `ret`；`0x0273` 解析委托给 `0x00442570`；`[SWITCH]` 行不可线性化 | `ClientStatic(hand)` |
| `.../proto-2.md:397–399,416,447` | NOTI_Town：`0x2EE bytes[50]`、`0x2EF bytes[5]`、`0x2F2 RAID_OTHER_CHANNEL_USER_TITLE_BOOK`、`0x4BD` 零长 | `ClientStatic(hand)` |
| `.../proto-3.md:484–509` | CMD `0x0332`/`0x0333` 响应共用 `0x0111F1F0`，零读取 | `ClientStatic(hand)` |
| `.../proto-5.md:144–166,233–242,280–311,363–369,626–628` | CMD 响应信封（`bSuccess`/`errorCode`）；`0x0293`/`0x0294`/`0x029E` 响应是空桩；`0x029B` 的错误码分支数组；`0x0299 u32`、`0x029A u8` | `ClientStatic(hand)` |
| `.../proto-6.md:192–240,484–490` | NOTI `0x02ED` 跨频道用户信息只读一组，u16 计数读后丢弃 | `ClientStatic(hand)` |
| `.../a21-units/unit-013.md:270` | 配置项 86 `CONFIG_LAST_RAID_CHANNEL_ON_CLIENT_CRASH`：客户端在本地记录崩溃前所在的 raid 频道，供重入使用 | `ClientStatic(hand)` |
| `.../a21-units/unit-029.md:330,479` | `CNRDStage` 有 「raid-mode-4-with-hitgauge」 绘制偏移；raid 模式的进图重定位分支未逆 | `ClientStatic(hand)` |
| `.../a21-vmp1/vmp1-5.md:201` | `CNRDDualRaidPacketManager.cpp` 属于房间→UDP 桥（双人 raid，不是本玩法） | `ClientStatic(hand)` |
| `.../a21-units/unit-035.md:300` | `anton` 是 SERVER_GROUP_TYPE 9（服务器组名），**与 raid 无关**，勿混淆 | `ClientStatic(hand)` |
| `DNF-JP-86-ClientHack/ClientHack/tools/literals-a21.csv` | 客户端源文件 `RDAR\CNRDRaidManager.cpp`（函数 VA `0x00C24DB0`，`CNRDRaidManager::moveChannel`）、`RDAR\PopupWindow\RaidChannelWindow.cpp`（VA `0x00F8FD40`）、`DNFShared\GameScript\RDARScriptRaid.cpp`（VA `0x02B29100`，解析 `[RAID BUFF LIST]`/`[RAID MONSTER LIST]`）；`MatchChannelMoveManager::moveRaidChannel` / `moveLastRaidChannel`（VA `0x00608AC0`/`0x00608BB0`）；窗口 `RaidCreateWindow`（含 `RaidNameEdit`）、`RaidListWindow2`、`RaidMemberWindow`、`RaidMatchingWindow`、`RaidWaitListWindow`、`RAID_CHANNEL_WINDOW`/`FINDRAID_WINDOW`/`RAID_TEAM_WINDOW`；局内 `anton_game_navigation_*`、`RaidPartyStatus`、`AntonBuff%02d`、`raid_timer`；过场 `Video/AntonCutScene/1-1…1-5,2-1…2-3,1-F,1-S,2-F,2-S.avi`；结算 `RaidRewardSelectCard`/`RaidRewardIndividual`/`RaidRewardDrawer`/`RaidTimeBorad`；`QUEST_EVENT_RAID_PHASE_CLEAR`（VA `0x00E68160`）；`not raid dungeon`（VA `0x028B3310`）；`Etc/Raid/Raid.lst`、`AntonStatusBoard.etc` | `ClientStatic(hand)` |
| 构包点与 handler 的地址邻近（VA = RVA+`0x400000`） | ① `0x0297`/`0x029A`/`0x029B`/`0x0332`/`0x0363`/`0x0293` 的构包点在 VA `0x00C22DB3–0x00C2CD39`，落在 `CNRDRaidManager.cpp` 区；② `0x0299` 构包点（VA `0x00F91EF7`）在 `RaidCreateWindow` 函数（`0x00F91BB0`，含 `RaidNameEdit`）内，dstring 应为团名；`0x029F`（`0x00F92007`）紧随其后；`0x029C`/`0x029E`/`0x0293`/`0x0334` 的构包点落在 `MyRaidBtn`/`MyRaidPanel`/`RaidMemberWindow` 区（`0x00F97390–0x00F9B1FE`）；③ `0x0328` 触发 `0x029D` 已走通指令路径，见 [重入分支](../../../../protocol/game/server-to-client/NOTI-0x0328.md)；④ `0x02B7` 构包点（VA `0x00A6DB63`）挨着 `RaidPartyStatus`/`PartyIcon`，buff 由局内小队状态面板发出；⑤ NOTI `0x0253` 的 [组队路径](../../../../protocol/game/server-to-client/NOTI-0x0253.md) 已走通，NOTI `0x024C` 的 handler（`0x0115E9A0`）紧跟 `00_start_back.ani` 的函数（`0x0115E890`） | `ClientStatic(proximity)` / Hypothesis |
| 服务端仓 `Docs/ProtocolScaffoldDissonances.md:56–62` | 上游把 raid behavior 转发到 NOTI `0x0296`，而客户端接收表里**没有** NOTI `0x0296`；NOTI 名称的置信度低于 CMD | `ClientStatic` vs `UpstreamReference` |
| 服务端仓 `Docs/新功能接入规范.md:314–320` | 安图恩团本翻牌不走 `.dgn [result card]`（安图恩的 `.dgn` 不声明该段），走哪条路未查明 | `PvfContent` |
| 服务端仓 `Docs/DungeonEntryNotes.md:58` | 全档 `[raid dungeon]` 14 个 | `PvfContent` |
| `Document/ChannelCatalog-20261010.md:7` | raid 组 type 32 的 CH201 原生客户端不接受，上游跳过；raid 环境=23 | `UpstreamReference` + 实测记录 |
| `Document/outputs/issue-101/pvf/dungeons.tsv` | 旧 PVF 的 dungeon 表，210–224 均带 `[raid dungeon]`、无疲劳、禁练习（与本次 dev PVF 一致） | `PvfContent`（旧版） |
| ClientEvidence | 有 A21 分析镜像，未发现团本现场 trace | `ClientStatic(hand)` |

暂无团本线级样本、运行时读取跟踪或实机观察。A21 多个 UI 和构包分支已经定位，跨会话的完整流程仍待实机。

## 3. 本仓现有 raid 相关实现（`CurrentImplementation`）

| 位置 | 现状 |
|---|---|
| `Server/src/Dfo.Server.Host/Resources/Channel/channel_info.etc:255–256` | 频道脚本：`200 <chn_channel_info_016> 23 [raid]`、`201 <chn_channel_info_037> 32 [raid]` |
| `Server/src/Dfo.Server.Infrastructure/Content/ChannelScript.cs:63,78,82` | `[raid]` 行只接受 type 23（CH201 type 32 被丢弃）；raid 频道的登录环境保留 23；CH200 容量 250 |
| `Server/src/Dfo.Server.Application/Channel/ChannelEntryDomainService.cs:104–105` | `LoginEnvironment == 23` → `ChannelEntryKind.Raid` |
| `Server/src/Dfo.Server.Data/Channel/ChannelModels.cs:39,65` | 枚举 `Raid`；`IsTransient` 包含 Raid：位置不持久化，离开后回原处 |
| `Server/src/Dfo.Server.Abstractions/Channel/IChannelSources.cs:37` | `RaidTownId = 19`，与 PVF `[WAITING ROOM] 19 2` 的城镇一致 |
| `Server/src/Dfo.Server.Application/World/EntryPlacementRuleService.cs:150–169` | 19 号城镇是特殊城镇：raid 频道只能落在 19，非 raid 频道不能进 19。落点取**城镇的 gate 区**（town 19 area 0 `Gate_BattleShip.map`），而 PVF 声明的等待室是 **area 2**（`BattleShip_Anton.map`，有 NPC 327/328/340）。两者不一致，需要实机判定落点 |
| `Server/src/Dfo.Server.Application/Quests/QuestObjectiveAuthority.cs:70` | `raid phase clear` 列在 `ClientCounted`：客户端上报即推进进度。服务端目前无从核对团本阶段通关 |
| `Server/src/Dfo.Server.Abstractions/World/DropRates.cs:13`；`Docs/DropRates.md:142` | 只用「安徒恩的抵抗者」做稀有度 6=传说的物证，与团本掉落无关 |
| `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs:381` | 注释：A21 的 `0x0300` 是 `LUKE_RAID_KILLED_NAMED_INFO`（卢克，A21 后内容），只是避撞 |
| 测试 | `ChannelAdmissionTests.cs:15`（CH200 type 23 → Raid）、`EntryPlacementTests.cs:26,235`（Raid → town 19 gate）、`ScriptChannelConfigurationTests.cs:30,55–57`（type≠23 的 raid 行被忽略）、`SessionGenerationOwnershipTests.cs:158,309`（Raid 频道的会话清理） |
| `Document/tasks/champion-promotion-355/README.md:40,46,70` | 精英提升按 PVF 处理，不做上游的安图恩团本特判（上游排除 210–215、218–224）。dev PVF 中 14 个 raid dgn 的 `[champion]` 全为 `0 0 0 0 0`，实际不会出精英 |
| `Document/plan/m16-normal-story-cycle-dungeons/milestone.md:15`、`normal-dungeon.md:23` | 明确把安徒恩排除在普通、剧情、循环地下城的里程碑之外 |
| 未实现 | 没有任何 raid 协议解包器或编码器，没有攻坚实例，`[raid dungeon]` 标志无人解析（`Server/src` 检索为零），`Etc/Raid/*` 无人读取，NPC 角色 `[start raid]`/`[leave raid zone]` 无处理 |

## 4. dev PVF 内容盘点（`PvfContent`）

### 4.1 文件

| PVF 路径 | 大小 | 作用 |
|---|---:|---|
| `etc/raid/raid.lst` | 10 | `0 Etc/Raid/Anton.etc`：raid 编号 0 = 安图恩 |
| `etc/raid/anton.etc` | 6720 | 团本主配置：人数、等待室、计时、门票、重试、阶段、子副本、触发器、奖励 |
| `etc/raid/anton.etc.buff` | 685 | `[RAID BUFF LIST]` 与 `[RAID MONSTER LIST]` |
| `etc/raid/anton.etc.symbol` | 410 | 符号编号表（对应 C→S `0x0297` / NOTI `0x0248` 的 symbolId） |
| `etc/raid/antonstatusboard.etc` | 260 | 攻坚状态板坐标与副本显示顺序 |
| `event/raidbuffevent.evt` | 290 | party/raid buff 活动表（含 CHN 活动痕迹，见 §4.12） |
| `dungeon/anton/phase1/*.dgn`（7）、`phase2/*.dgn`（7） | — | 团本子副本，见 §4.3 |
| `dungeon/anton/table/monsterapcdifficultybonus_raid1.tbl`、`_raid2.tbl`、`_normal.tbl` | 84690 各 | 一阶段与二阶段的怪物、APC 难度加成表 |
| `town/BattleShip_Raid.twn`（town 19）、`map/town/battleship/battleship_anton.map` | — | 攻坚等待城镇 |
| `itemshop/naen_raid.shp`（shop 108）、`itemshop/wuun_raid.shp` | — | 门票商店、团本兑换商店 |
| `ui/raidwindow/*.xui`、`ui/raid/anton/**`、`ui/event/antonraidopeneventwindow/*.xui` | — | 招募/频道/成员/等待/匹配窗口，局内导航，结算翻牌（UTF-16 XUI，本次未展开） |
| `map/town/slough/slough_antonraid.map` | 765 | 存在，town 19 未引用，用途 `Unknown` |

### 4.2 `anton.etc` 全局参数

| 标签 | 值 | 备注 |
|---|---|---|
| `[PHASE MAX]` | 2 | |
| `[RAID MEMBER MAX]` | 20 | |
| `[RAID START MINIMUM MEMBER]` | 10 | |
| `[WAITING ROOM]` | `19 2` | town 19 苍穹贵族号，area 2 `BattleShip_Anton.map` |
| `[WAITING ROOM NPC]` | `326 327` | 326 奈恩（npc_naen，商店 108），327 鹰眼杰克特（role `[leave raid zone] 0 [start raid] 0`，field role「攻坚开始」） |
| `[DISCONN MEMBER WAIT TIME]` | 300 | 秒（推断） |
| `[START DELAY TIME]` | 3 | |
| `[PHASE TIME OVER]` | `2400 2400` | 每阶段 40 分钟 |
| `[PHASE BREAK TIME]` | 600 | 阶段间歇 |
| `[ENTRY COST]` | `10096296 1` | 强力的魔能吸食阻断装置 ×1 |
| `[RETRY]` / `[RETRY ITEM]` | `1` / `10099897 1` | 皇室的支援证书。物品说明是「重置奈恩处阻断装置的每日购买限制」，与 RETRY 的确切关系 `Unknown` |
| `[SHIELD CHARGE RATE]` | `17 34` | |
| `[HATCHERY INFO]` | `4 3 221 222 223 224` | 4 个孵化中心 |
| `[EXCEPT CHEAT DUNGEON]` | 220 | |

### 4.3 阶段与子副本

`[DUNGEON INFO]` 给出 `INIT STATE`、`[<id> DGN HP]` 与 `PARTY MAX`。`.dgn` 共同字段：`[minimum required level] 85`、`[basis level] 90`、`[raid dungeon] 1`、`[designate dungeon difficulty] 2`、`[coin limit] 12`、`[enter without fatigue] 1`、`[no fatigue]`、`[no giveup panalty]`、`[prohibit practice] 1`、`[escape hell] 1`、`[recommend party] >4×5`、`[champion] 0 0 0 0 0`、`[worldmap pattern info] 1 4 WorldMap/SelectdungeonSlot/anton.img <n>`。

| 阶段 | id | PVF 路径 | 名称 | init | HP | 小队上限 | 网格 / 起点 / Boss 格 | 命名怪 / Boss（`anton.etc.buff`） |
|---|---:|---|---|---|---:|---:|---|---|
| 1 阻截安徒恩 | 210 | `dungeon/Anton/Phase1/Close_Blacksmoke.dgn` | 黑雾之源 | open | 4 | 5 | 4×1 / 0,0 / 3,0 | 吞噬魔 58529 / 歼灭之内尔贝 65664 |
| 1 | 211 | `.../Close_Blacksmoke2.dgn` | 黑雾之源 | hide | 1 | 5 | 4×1 | 同 210 |
| 1 | 212 | `.../Strongleg_High.dgn` | 震颤的大地 A战区 | hide | 1 | 5 | 3×2 / 1,1 / 1,0 | 毁灭之塔伊奥利 59055、湮灭之弗雷伊内斯 56635 |
| 1 | 213 | `.../Strongleg_Lower.dgn` | 擎天之柱 A战区 | hide | 2 | 5 | 1×5 / 0,4 / 0,0 | 火焰之克瑞夫 56636、守门将克拉特 58020 / 炽炎之艾格尼丝 64062 |
| 1 | 214 | `.../Strongleg_High2.dgn` | 震颤的大地 B战区 | hide | 1 | 5 | 3×2 | 同 212 |
| 1 | 215 | `.../Strongleg_Lower2.dgn` | 擎天之柱 B战区 | hide | 2 | 5 | 1×5 | 同 213 |
| 1 | 216 | `.../Fleet_Fire.dgn` | 舰炮防御战 | hide | 1 | **1** | 2×1 / 1,0 / 0,0；另有一处 `[coin limit] 4` | — |
| 2 灭杀安徒恩 | 218 | `dungeon/Anton/Phase2/EnergyCut.dgn` | 能量阻截战 | open | 1 | 5 | 4×5 / 3,2 / 0,2；有 `[randomized object creation]` | 吞噬之厄伽勒 69311 |
| 2 | 219 | `.../BlackVolcano.dgn` | 黑色火山 | open | 1 | 5 | 5×2 / 0,1 / 4,1 | 黑烟之克雷斯 59058、魔刹石伊弗 58024、深渊之梅迪尔 58021 / 全能之玛特伽 64061 |
| 2 | 220 | `.../AntonHeart.dgn` | 安徒恩的心脏 | hide | 5 | 5 | 2×1 / 0,0 / 1,0（map 17193/17194） | （EXCEPT CHEAT） |
| 2 | 221–224 | `.../Hatchery1–4.dgn` | 第一至第四孵化中心 | hide | 1 | 5 | 4×1 或 1×4 | 命名 58531 熔岩怪虫 / 58027 梅尔塔 / 56638 粉碎之阿托尔 / 56764 恐怖之邪念体；感染 58533 光剑洛克、58532 巨斧克洛 |

217（`map/217_dalelidhole`）不在 `dungeon.lst`。一阶段用 `MonsterApcDifficultyBonus_raid1.tbl`，二阶段用 `_raid2.tbl`。

#### 218 到孵化中心的传送

dev PVF 的 `dungeon/anton/phase2/energycut.dgn`（218）在四角地图放「安徒恩的筋」。`passiveobject/actionobject/map/anton/phase3/wall/action/wall_last{,_1,_2,_3}.act` 检查目标图状态是否为 0，满足后生成 18888–18891 的传送对象；相应 `wall_open*.obj` 带 `[warp object]`、`[int data] 0 150 <目标图> 2 20000` 与 `[string data] party include self`。

| 218 房间地图 | 入口被动对象 | 目标图 |
| --- | --- | --- |
| 19100 | 18862 | 224 |
| 19103 | 18863 | 221 |
| 19116 | 18893 | 223 |
| 19119 | 18892 | 222 |

A21 战况导航入口 VA `0x00A4E7C0` 的进图分支在 `0x00A4E952–0x00A4E961` 排除 221–224。dev PVF 证实局内传送对象及目标图，真实触发条件、换图报文与小队在源局的处理由实机确认。

### 4.4 触发器（`anton.etc` `[TRIGGER]/[BEHAVIOR]`，摘要）

指令词汇：`IF`、`CHECK DUNGEON STATE <id> open|clear|close|hide`、`ON CHANGE DUNGEON STATE <id>`、`ON CHANGE SYMBOL <sym>`、`CHECK TIMER END <type> <id>`；动作有 `SET DUNGEON STATE`、`RESERVE DUNGEON STATE <id> <state> <sec>`、`SET <sym> <v>`、`SET TIMER <type> <id> <sec>`、`RESET TIMER`、`CLEAR PHASE`、`OPEN RANDOM HATCHERY`。计时器类型 1/2/3 与 NOTI `0x0255` 的 key0 取值同域（推断）。

| 阶段 | 主线 |
|---|---|
| 1 | 210 通关 → 210 隐藏，211 关闭，212–215 开放，216 在 20 秒后开放，黑雾开、播黑雾出动画，`SET TIMER 2 211 300`。211 开放后计时 240 秒循环生成黑雾被动（SMOKE PASSIVE），清后关闭、300 秒后重开，重开后 480 秒内未清则 `1PHASE FAIL`。216 开放播「舰炮」动画，360 秒未清则 NAVIGUN FAIL、KILL ALL CHARACTER、212–215 重开、213/215 HP 归零；清了则 150 秒后重开。212/214 开放 300 秒内未清 → `1PHASE FAIL`，清后关闭 150 秒再开。**213 与 215 都 clear → `1PHASE SUCCESS`** → 全部隐藏 + `CLEAR PHASE`。`1PHASE FAIL` → 全灭、回到 210 open 的初态 |
| 2 | 初始化：`2PHASE INIT=1`、`BLACK VOLCANO BARRIER=10000`、`SET TIMER 3 219 180`。屏障到 10000 播恢复动画，到 0 播破碎动画（上游用 C→S `0x0297` 更新 110，A21 实际调用场景待证）。218 通关后保持 open、HP 归零。219 的计时 3 结束 → `OPEN RANDOM HATCHERY`。孵化场开放后 120 秒未清 → `<id> HATCHERY FAIL`（每 45 秒重复），120 秒和 50 秒周期生成命名怪；清后关闭 240 秒再开。**219 clear → 218/219/221–224 隐藏，220 开放**；**220 clear → 220 隐藏 + `CLEAR PHASE`** |

### 4.5 `anton.etc.buff`

| buff | 作用域 | 三个参数（含义 `Unknown`；按常见读法依次为冷却/持续/次数或效果值） | C→S `0x02B7` 上游编号（`UpstreamReference`） |
|---|---|---|---|
| ATTACK BONUS | RAID | `420 30 15` | 1 |
| INVINCIBLE | PARTY | `360 30 0` | 2 |
| RESTORE | PARTY | `240 60 7` | 3 |
| INCREASE TIME | RAID | `7200 10 300` | 0 |
| INCREASE COIN | PARTY | `300 10 4` | 4 |

### 4.6 `anton.etc.symbol`（id → 符号）

`1` SMOKE PASSIVE CREATE；`2` METEO PASSIVE CREATE；`3–6` 221–224 HATCHERY FAIL；`7` IS EXIST INFECTION DUNGEON；`8` INFECTION DUNGEON INDEX；`9–12` 221–224 HATCHERY NAMED CREATE；`50–63` 210–224 DGN HP（不含 217）；`102` KILL ALL CHARACTER；`103` KICK OUT CHARACTER；`104` 1PHASE FAIL；`105` 1PHASE SUCCESS；`106` 2PHASE INIT；**`110` BLACK VOLCANO BARRIER**；`120` DGN SMOKE OPEN；`121–128` MOVIE SMOKE IN/OUT、NAVIGUN ON/SUCCESS/FAIL、BARRIER BREAK/RECOVER、HATCHERY OPEN。

### 4.7 `antonstatusboard.etc`

`[SCREEN POS] 400 300`，`[VIDEO POS] 250 50`，`[DUNGEON INDEX] 210 211 216 213 215 212 214 219 218 220 221 222 223 224`，`[DUNGEON POS]` 是 14 组坐标。客户端据此绘制攻坚状态板，副本 id 集合即 NOTI `0x024A/0x024B/0x0249` 的副本域。

### 4.8 城镇、NPC 与商店

| 对象 | PVF | 关键内容 |
|---|---|---|
| town 19 | `town/BattleShip_Raid.twn` 苍穹贵族号 | area 0 `Gate_BattleShip.map`（`[gate] 474 234`），area 1 `BattleShip_Main.map`（`[normal]`），area 2 `BattleShip_Anton.map`（`[dungeon gate] 27`，gate 27 的含义 `Unknown`）；三区都要求 85 级 |
| `battleship_anton.map` | `map/town/battleship/battleship_anton.map` | `[NPC] 327 / 328 / 340`；`[town movable area]` 末项 `19 1`，可回到 area 1 |
| NPC 326 | `npc/npc_naen.npc` 奈恩·希格 | `[item shop] 108` = `naen_raid.shp`：「团队模式入场材料」tab 卖 10096296；消耗品 tab 卖 10095687–10095690 和 10146786–10146789；材料 tab 卖 3341 |
| NPC 327 | `npc/npc_zactor.npc` 鹰眼杰克特 | `[role] [leave raid zone] 0 [start raid] 0`：开团与离开攻坚区由 NPC 角色触发 |
| NPC 328 | 乌恩·莱奥尼尔 | `[item shop] 110`；`wuun_raid.shp` 分「安徒恩普通模式」「安徒恩团队模式」「安徒恩史诗武器」三个 tab |
| NPC 330 | `npc/hiram.npc` 海岚·克拉夫 | town 19 area 1 `battleship_main.map` 的甲板右侧，地图坐标 `(1304, 241)`。`[create raid] 1`，字段菜单「创建攻坚队」 |
| NPC 340 | 天界分解修理机 | 商店 96 与分解 |
| NPC 332 | 奈恩·希格（另一个实例） | `[item shop] 107`；任务 12829 的发放 NPC |

### 4.9 门票与材料

| id | 名称 | 关键字段 |
|---|---|---|
| 10096296 | 强力的魔能吸食阻断装置 | 「安徒恩团队模式入场材料」；`[material]`；`[need material] 3341 50`；`[price] 200000`；`[stack limit] 1`；`[daily purchase limit] charac 1`；`[daily delete item] 1`；不能入仓或寄售（`charac cargo ban redeemitem`） |
| 3341 | 魔刹石 | 兑换门票的材料 |
| 10099897 | 皇室的支援证书 | RETRY ITEM；重置每日购买限制 |
| 10095687–10095690 | 梦幻的世界仪、至尊异常/虚弱/控制解除药剂 | 说明写明「无法用于普通、暴走安徒恩以及攻坚队地下城」 |

### 4.10 奖励（`[CLEAR REWARD ITEM]` / `[STATE REWARD]`）

行格式为 `<池> <参数A> <参数B> <itemId> <标志>`。池名有 `party_card`、`gold`、`pcroom_card`、`squad_item`。参数 A 在 gold 行取 -1/2/3，对应 C/A/S 级金罐（名称可证）；在其他池恒为 -1。参数 B 在 card/squad 行像权重。末列 0/1 的语义 `Unknown`。所有奖励物品都是 `[upgradable legacy]` 容器，靠 `[int data]` 展开成真实物品。`[CONDITION FOR RANK]` 的死亡次数分档两阶段相同：`0–1→0`、`2–5→1`、`6–9→2`、`10–14→3`。

| 阶段 | 池 | 行（参数 A，参数 B，itemId，标志） | 物品名 / 展开 |
|---|---|---|---|
| 1 | party_card | -1, 1, 10094735, 0 | 浓缩的魔能石（3~4 个），展开为 10093974 ×3/×4 |
| 1 | gold | -1/2/3 → 10094732 / 10094734 / 10094786 | 第 1 阶段 C/A/S 级金罐，`int data 0 100 50000`（C）…`80000`（S） |
| 1 | pcroom_card | -1, 1, 10094733, 0 | 浓缩的魔能石 1 个 |
| 1 | squad_item | 50 / 31 / 3 / 1 / 15 → 10157773 / 10157774 / 10157775 / 10157776 / 10157777 | 深渊派对邀请函（125 张）、魔能石（3~5）、安徒恩传说装备、安徒恩传说卡片、史诗装备（Lv60~85） |
| 2 | party_card | 65 / 31 / 3 / 1 → 10157778–10157781 | 邀请函（125 张）、魔能石（1~5）、安徒恩传说装备、安徒恩传说卡片 |
| 2 | gold | -1/2/3 → 10094789 / 10094788 / 10094787 | 第 2 阶段 B/A/S 级金罐（S=`0 100 160000`） |
| 2 | pcroom_card | -1, 1, 10094733, 1 | 同上 |
| 2 | squad_item | 775 / 145 / 50 / 25 / 5 → 10157782–10157786 | 史诗装备（Lv60~85）、安徒恩的灵魂碎片（10095702）、源助力装备（武器）、安徒恩史诗装备（贪食/源助力）、安徒恩的心脏卡片（10095701） |

翻牌 UI 显示 4 张卡（`reward_card_open*`、`RaidRewardSelectCard`），对应 C→S `0x0295` 的 cardIndex 0..3（`UpstreamReference` + 资源名）。10095702 灵魂碎片可在乌恩处兑换史诗装备或安徒恩设计图材料。

### 4.11 任务

| quest | PVF | 类型 / int data | 说明 |
|---|---|---|---|
| 12829 | `n_quest/...`（npc 332） | `[seeking] 3262 100` | 「[团队安徒恩]异次元的安徒恩」：完成后**可以进入安徒恩团队模式频道**（频道准入条件） |
| 12830 | `n_quest/Anton/Anton_Epic_Acce_01.qst` | `[raid phase clear]`，sub 0，`0 5 -1 1 5 -1` | 「[团队安徒恩]贪食之巨兽」，85–99 级，前置 12829，npc 326；阻截与灭杀各 5 次，团队模式频道内每天只计 1 次；奖励五选一 `100300212/100312095/100321954/100343175/100352106` |
| 6742 / 6743 / 6744 | `n_quest/Title/titlebook_85_antonraid_title_2/3/4.qst` | `[raid phase clear]` `1 15 2` / `1 15 1` / `1 15 0` | 以队员、小队队长、攻坚队队长身份通关 15 次（以二阶段为准）：**int data 第 3 位是身份 2/1/0** |
| 8514 / 8515 | `n_quest/Event/chn_event_2015_anton_raid_title_event_*.qst` | `[raid phase clear]` `0 5 -1` | CHN 活动残留 |
| 6741 | title_1 | `[custom quest] 1 0 0` | 挑战 30 次 |
| 6745 / 6746 | title_5 / 6 | `[condition under clear]` sub 6 `219 -1` / sub 4 `-1 -1` | 黑色火山 30 次 / 无复活币 |
| 6747 | title_7 | `[seeking]` 前 6 个称号 | 全称号 |

`[raid phase clear]` 的 int data 推断为 `<阶段 0|1> <次数> <身份 -1|0|1|2>`；12830 有两段，分别对应阻截与灭杀（Hypothesis）。全档只有这 6 个任务用该类型。客户端字面量 `QUEST_EVENT_RAID_PHASE_CLEAR` 存在。

### 4.12 范围外但同名的内容

| 内容 | PVF | 处理 |
|---|---|---|
| 安徒恩普通 225–233、任务版 234–238、觉醒 243–247 | `dungeon/Anton_Normal|Anton_Quest|Anton_Awakening/**` | 普通副本族，不是团本，留给「安徒恩普通/觉醒」里程碑 |
| 双人 raid | `dungeon/event/dual/dual_raid*.dgn`、`event/dualraid/dualraid.info`、`ui/dualraid/**`、`map/twin_raid_*`、协议 `DUAL_RAID_*` | 不在 `dungeon.lst`，与本玩法无关 |
| CHN 活动 | `stackable/event/chn_event_20150312_anton_raid/*`、`event/raidbuffevent.evt`、字面量 `Anton_Raid_Event_Buff_Chn`、`ANTON_RAID_OPEN_EVENT_CHN` | 区域残留，默认不纳入 |

## 5. 普查文档的范围

`Document/A21玩法与系统全集第一轮普查.md`：

| 位置 | 内容 |
|---|---|
| :139 | 「安徒恩普通与觉醒」单列为另一个候选 |
| :140 | **安徒恩 Raid** 纳入，范围是「攻坚队；频道；队伍编成；阶段/地图；能源/阻截；攻坚状态；重试；奖励；竞拍/分配」。证据：日服 2016-04 已在改造已有的安徒恩 Raid（`JP-Live` 线索）；「A21 协议和上游 Raid 面完整」（`ClientStatic` + `UpstreamImplemented`） |
| :218 | 正式目录第 23 项「安徒恩 Raid」 |
| :38、:184、:190 | 上游有 Raid 模块；卢克 Raid 属于 `ExcludedPostA21`；opcode 中的后期 Raid 名不直接入任务 |

普查只给了范围清单，没有协议、PVF 或客户端的细节证据。其中「竞拍/分配」在 dev PVF 的 `anton.etc` 里**没有对应段**，只有翻牌池与 squad_item，属于 `Unknown`。

## 6. 主要缺口

| # | 缺口 | 为什么重要 |
|---|---|---|
| 1 | 暂无团本线级样本或实机观察 | catalog 已登记静态证据；实际发送时序、受众和画面须另证 |
| 2 | 生成区仍有旧上游偏移和过高置信度（§1.5） | 编解码以协议页的 A21 人工记录为准 |
| 3 | 部分打包区的 NOTI（含 `0x02EA`、`0x0273` 委托体、`0x02EC`）尚未闭合；`0x0258` 已追到 UI 路径，实际帧体仍待测 | 首版只接已明确需要的边界 |
| 4 | C→S `0x0298` 构包经虚表，`0x0334` 结构未知 | 进入相关场景时抓包 |
| 5 | 多条 CMD 响应为空桩。已确认 `0x029D` 只读结果信封、`0x0364` 成功读额外 u32；`0x0334` 待取证 | 结果信封、NOTI 投影和 UI 推进分别验证 |
| 6 | 已定位单个客户端处理分支，尚无整段生命周期证据 | 靠 [实机取证场景](../design.md#82-a21-实机取证) 验证跨会话顺序 |
| 7 | 借出小队在客户端怎样显示（普通队伍 NOTI 还是 raid 专用 NOTI）没有任何客户端证据 | 直接决定 AntonRaidDesign 的投影层 |
| 8 | 落点：本仓用 town 19 的 gate 区，PVF 等待室是 area 2 | 需要实机核对 NPC 327 是否可达 |
| 9 | 频道准入：PVF 有「完成 12829 才能进团队模式频道」，本仓 raid 频道没有任务门槛 | 规则缺失 |
| 10 | `raid phase clear` 在本仓是客户端计数 | 团本落地后应改为服务端裁决，或至少与攻坚实例交叉核对 |
| 11 | `[STATE REWARD]` 列语义、末列标志、`pcroom_card`、阶段 rank 与金罐等级的映射、`[RETRY]`、`[DUNGEON INFO]` 的 `HP` 含义、`[dungeon gate] 27`、buff 三参数、翻牌 UDP 包 `UDPPACKET_ANTON_REWARD_SELECT_CARD` 的作用 | 结算与 buff 的数值规则都未定 |
| 12 | NOTI `0x0254`（推断 START_RAID）、`RAID_PHASE_COIN_INFO`、`ANTON_CONQUEST_CLEAR_COUNT` 有名无 handler | 避免照上游去发客户端不处理的包 |

## 7. 复现

```bash
# 构建到临时目录，不写入仓库
dotnet build Tool/PvfProbe/PvfProbe.csproj --artifacts-path <tmp>
export PVF_ARCHIVE_PATH='E:\Project\DNF\ClientStore\mounts\dev\content\Script.pvf'
PvfProbe dump etc/raid/anton.etc 5000          # 另有 anton.etc.buff / .symbol / antonstatusboard.etc / raid.lst
PvfProbe lst dungeon/dungeon.lst 210 3000      # 210–224
PvfProbe lst town/town.lst 19 600
PvfProbe lst npc/npc.lst 327 300
PvfProbe lst itemshop/itemshop.lst 108 300
PvfProbe lst stackable/stackable.lst 10096296 400
PvfProbe lst n_quest/quest.lst 12830 400
PvfProbe where n_quest/quest.lst "[type]" "[raid phase clear]" 100
PvfProbe files raid ; PvfProbe files anton
```

协议页摘要由脚本从 `protocol/game/**` 机器区抽取。客户端字面量可用 `grep -i raid DNF-JP-86-ClientHack/ClientHack/tools/literals-a21.csv` 复现。
