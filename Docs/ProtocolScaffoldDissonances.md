# 协议脚手架建设中浮现的失谐

2026-08-16 建设 [Game 协议全量索引](../protocol/game/index.md) 时，把客户端静态提取的 body
布局与上游服务端（`D:\project\86JP`）的解析/构造代码逐条对位，发现下列不一致。

**这里只记录，不修改。** 每一条都需要一份实机样本才能判定哪一侧是对的，而样本要等游戏走到
那个状态。过早"弥合"只会把一个猜测固化成两处代码。凡受影响的字段，协议文档里已按
`留空 = unknown` 处理，不会把任一侧当作事实。

## 一、重复组的位置模型两侧不一致（影响面最大）

客户端提取按指令地址还原字段序，重复组落在循环所在处；上游多数 builder 却把可变项**插在
计数字节之后、固定尾部之前**。两种模型在 `n = 0` 时一致，`n > 0` 时其后所有偏移都会漂移。

| 协议 | 客户端提取 | 上游 |
|---|---|---|
| `NOTI 0x0002` USERINFO | 组宽 15B/条 | `WriteAppearanceEntry` 23B/条（多 `i32 ExpansionLen + 4B`） |
| `NOTI 0x0025` EXP | 组排在固定段之后 | 可变项插在 offset 50 的计数之后 |
| `NOTI 0x0023` | 一个 u8 在 offset 126 | `DungeonNotificationBuilder` 是连续两字节 117/118 |
| `NOTI 0x00D1` | `2 + 9n` | `BuildReward` 是 `3 + 8n + 3` |

**影响**：重复组之后的顶层偏移在文档里带 `*` 标记，表示**名义值**——累加时按重复次数
`n = 0` 排布，真实位置 = 名义值 + Σ(组宽 × 实际重复次数)。这类偏移**仍然接受字段含义**：
位置不确定不代表字段身份不确定，上游代码往往能确证"这个字段是什么"，只是"它在第几字节"
要等计数确定。落在名义偏移上的含义，其证据列会注明「位置为名义值」。

早先本节曾写"这类偏移一律显示为 `?` 且不接受字段含义"。那描述的是一个更早的版本；
按 `?` 处理会连带丢掉上游已经证实的字段身份，因此改为名义值 + 显式标注。要取真实偏移，
仍然必须有样本。

## 二、提取器选中的构包点归属另一个 opcode

`CMD 0x031C` 的最优构包点是 `8 + n×7` 形态，而其分发目标
`Handle_EQUIPMENT_EMBLEM_ATTACH`（`InventoryHandler.Equipment.cs:432`）解析的是 `7 + n×7`、
无前缀字节。与 `8 + n×7` 逐字节吻合的上游代码是 `TryParseEmblemAttachBodyAt(body, 1, …)`，
它挂在 `CMD 0x00C9` AVATAR_EMBLEM_ATTACH 上。

同类：`CMD 0x00A0` 名为 `USE_BOOSTER_ITEM`，实际分派到 `Handle_OPEN_SELECTABLE_PACKAGE`；
`CMD 0x0218` 名为 booster，实际走 `TrySaveCrystalContractSelection`；
`CMD 0x03EA` 名为 `BIND_PLUS`，实际是 `Handle_COMPOUND_AVATAR_SET`。

**结论**：**枚举名给"叫什么"，分发表给"做什么"，两者可以不一致。** 实现前必须跟进分发目标，
不能按名字推语义。

## 三、上游存在零调用点的 builder，且它才与客户端吻合

`NOTI 0x0184`：在跑的发送点用 `TournamentPacketBuilder.BuildTournamentMapInfo`，产出 13 字节、
无重复组；而客户端提取的 `27 + n×18` 与 `Network/Builders/Party/DungeonRoomP2pBuilder.cs`
逐字段完全吻合——那个 builder 在整个上游树里**零调用点**。两者在前 11 字节上互相冲突
（offset 2 一个是 `seed`、一个是 `partyId`）。

**推测（未证实）**：上游改过该 type 的用途，旧 builder 留在树里没删。客户端仍按旧格式解析。

## 四、上游 `NotiPacketType` 与本客户端的对应存在个例偏差

`NOTI 0x0247`：上游枚举里 `0x0247 = MODULE_REQUEST`、`RAID_DO_BEHAVIOR = 0x0296`，但
`RaidHandler.cs:167` 把 raid behavior 转发到 `0x0296`，而**客户端接收表里根本没有
NOTI `0x0296`**。

**边界**：CMD 族的名称已由 16 条已双向验证协议交叉核对、零错位；NOTI 族的锚点较少
（`0x0289`/`0x00AD`/`0x01C7`/`0x0187`/`0x01BA`/`0x0002` 均吻合）。上述个例不足以推翻枚举，
但**NOTI 名称的置信度应低于 CMD**，遇到行为对不上时先怀疑名称。

## 五、上游要求的 body 长度与客户端构包长度冲突

| 协议 | 客户端 | 上游 |
|---|---|---|
| `CMD 0x0128` | 20B，含条件分支 | `SecretShopBuyRequest.cs:19` 要求恰好 8B，否则整条拒绝 |
| `CMD 0x0257` | 闭合 11B，无字符串字段 | `CreateExpertJobStoreRequest` 要求 `15 + nameLen` |
| `CMD 0x02CA` | 1B | `InventoryHandler.Move.cs:220` 要求 ≥ 3B |
| `NOTI 0x0016` | 9B（offset 7 为 u16） | `BuildUserPosition` 写 8B（末位 u8） |
| `NOTI 0x0104` | 6B | 血之祭坛路径只发 1 个零字节 |
| `NOTI 0x02AA` | 259B | `BuildRejoinableDungeon` 只写 4B |

`0x0257` 这一条直接导致了提取器的一处修正：`walk_builder` 原先静默跳过未识别的直接调用，
写 dstring 的辅助函数会被整段漏掉却仍报告"布局已闭合"。现已改为跟进调用，跟不动的显式降级
为「布局部分可得 / 构包路径上有跟不下去的调用」。

## 六、上游对相当一部分 CMD 根本不解析 body

`0x0003 EXIT`、`0x0007`、`0x0009`、`0x0025`、`0x0028`、`0x000F`、`0x0060`、`0x0131`、
`0x0198`、`0x007B`、`0x013C`、`0x0270`、`0x026B`、`0x02A8`、`0x0312`、`0x035D` 等，
handler 要么只把 body 打进日志，要么完全不引用它。

**这对服务端实现是好消息**：这些 type 只需接收并回帧（若客户端注册了响应 handler），
不必理解 body。但**不能反推"客户端也没发内容"**——客户端构包点确实写了字段。

## 七、`NOTI 0x0002` 子类型 `0x02` 在本仓有两个编码器

角色列表界面走 `Dfo.Server.Protocol/Game/AccountRoster/AccountCharacterRosterPayloadCodec`，
入场序列走 `Game/StateSynchronization/SupplementalSynchronizationCodec.EncodeRoster`。
两者产出的是**同一条线级事实**——同一个 type、同一个子类型、同样的头部与条目形状——
却各写一份，包括各自的 `WriteRosterTail`。

已经因此付出过一次代价：A21 把条目尾部从 32 字节改为 36 字节时，两处都要改，
少改一处就会在两个角色的账号上丢掉第二个角色，而单角色账号看不出来。

两者的输入也不同：界面那条拿 `CharacterSummary`（本次为它补上了外观条目），
入场那条拿 `CharacterRosterMemberState`（外观来自逐角色载入的完整入场状态）。
后者顺带把等级之后那十字节建模为两个进度值加一个保留字段，而界面那条按实测字节写
`0x0B 0x01` 加两个零——**同一段字节，两种读法并存**，这一条本身也需要一个样本来收口。

**这里还有一个今天就存在的不一致**：已知良好会话在入场序列里重发的那一帧，与角色列表界面
那一帧**逐字节相同**（含等级之后的 `0B 01`）。而本仓入场路径现在写的是两个进度值，
除非某角色的首个进度值恰为 267，否则它发出的字节与该会话不同。角色列表那条已经对齐，
入场那条**尚未**——它属于入场序列成果的范围，在那之前这条差异是已知的。

按 `AGENTS.md` 的切片规则，共用的东西应当上提到 endpoint 层或领域服务。**本文只记录，
不在协议对齐任务里动它**：合并两个编码器属于结构调整，应当单独立项并单独评审。

## 下一步

上述每一条的判定证据都是同一种：**一个实机样本**。走到相应游戏状态后，按
[方法论](../protocol/methodology.md) 抓包并回填对应协议文档，再回来划掉这里的条目。
