# spec-003 进度记录（实现阶段中途）

本文件不是工作流制品，是一次中途检查点的如实记录，便于接手时不必重跑诊断。

## 已完成并在线上确认

| 改动 | 位置 | 线上验证 |
|---|---|---|
| `ENTER_GAMEWORLD_COMPLETE` 报文体 0 → 8 字节（`01 00 00 00 01 00 00 00`） | `StateSynchronizationMessages.cs` 的 `EntryMarkerMessage` | 实机 body=8 |
| 入场批次里补发 USERINFO 子类型 `0x06`（25 字节），并停止在标记之后重发子类型 `0x00` | 新增 `TownReadyStateMessage`；`SelectedCharacterStateMessageFactory` 换位 | 实机 body=25、子类型 0x06 |
| 子类型 `0x00` 在版本之后补 38 个零字节 | `StateSynchronizationPayloadCodec.Entry.cs` 的 `EncodeCurrentUserState` | 实机 body 由 134 变 172 |

## 最重要的一条：入场批次有八个协议发在了 A12 的旧号上

这条推翻了 spec-001 里我下的一个结论。当时我核对的是 `GameProtocol.cs` 里的 44 个常量，
它们确实全都落在 A21 的编号位移点之下，于是我写下「编号零改动」。**但入场状态消息各自
硬编码自己的 type，根本不在那个文件里**，其中八个落在位移点之上。

入场批次的 45 个 opcode 里有 9 个在两版之间含义不同；我们发错了其中八个：

| 我们发在 | 我们的意思（A12 名） | A21 在该号上的含义 | A21 的正确号 | 位移 |
|---|---|---|---|---|
| NOTI `0x02D5` | `DAILYSCHEDULE_CONTENTS_STATE` | `ACCOUNT_TUTORIAL_INFO` | `0x033F` | +0x6A |
| NOTI `0x02DA` | `BUY_RESTRICT_ITEM_LIST` | `YUT_GAME_EVENT_INFO` | `0x0344` | +0x6A |
| NOTI `0x0300` | `CONTRACT_OF_CUBE_INFO` | `LUKE_RAID_KILLED_NAMED_INFO` | `0x036A` | +0x6A |
| NOTI `0x0331` | `UPGRADE_CARRY_GOLD` | `LIMIT_IPGNO` | `0x039B` | +0x6A |
| NOTI `0x0357` | `EQUIPMENT_RENTAL_LIST` | `TOURNAMENT_END` | `0x03C1` | +0x6A |
| NOTI `0x0381` | `COLLECT_BOX` | `USER_BUY_RESTRICT_UPDATE_ITEM_LIST` | `0x03EB` | +0x6A |
| NOTI `0x03D8` | `INCREASE_CHANCE_LOTTERY_ALL` | `COUPLE_ROOM` | `0x0442` | +0x6A |
| CMD `0x0312` | `PREMIUM_SERVICE` | `CHANGE_GUILD_MARK` | `0x036F` | +0x5D |

七条 NOTI 的位移**完全相同**（+106），CMD 那条是 +93——即 A21 在各自分界点之前插入的条目数。
这个一致性本身就是该映射正确的旁证。

**这也解释了此前那份「缺发 9 条、多发 6 条」的清单**：它根本不是缺发多发，是同几条协议
发在了旧号上，于是在新号上显得缺、在旧号上显得多。改完之后两组各自消失。

八条已按 A21 表改正（`StateSynchronizationMessages.cs`），每处都注明了旧号在 A21 上属于谁。

**这条结论要回写进 spec-001 的协议记录**：「A21 编号对我们已实现的协议零改动」这句话
只对 `GameProtocol.cs` 里那 44 个成立，对入场状态消息不成立。

## 方法上的转折：参考抓包才是权威，opcode 表推不出真相

我此前用「A21 表在分界点之上统一位移」推出八处重编号。**其中四处是错的**，并且是那份
参考抓包（ServerS4A21 的 458 帧完整会话）直接判的：

| 我改成 | 参考实际用 | 判据 |
|---|---|---|
| `0x033F` | **`0x02D5`** | 两边都是 3×16 字节 |
| `0x039B` | **`0x0331`** | 两边都是 1 字节 |
| `0x03EB` | **`0x0381`** | 两边都是 2×8 字节 |
| `0x0442` | **`0x03D8`** | 两边都是 **204 个零，逐字节相同** |

204 个零逐字节相同不可能是巧合。真相是：**这四条的载荷当年就是照这份抓包在这些号上抄下来的**，
号一直是对的，错的只是我们按 A12 表给它起的名字（A21 管它们叫 ACCOUNT_TUTORIAL_INFO、
LIMIT_IPGNO、USER_BUY_RESTRICT_UPDATE_ITEM_LIST、COUPLE_ROOM）。四处已回退，注释改成
「A21 叫甲，A12 表叫乙，号以抓包为准」。

另三条 NOTI（`0x0344` / `0x036A` / `0x03C1`）和 CMD `0x036F` 保留——A21 表和抓包**双向**印证。

教训：opcode 表能给假设，只有抓包能定论。凡两者冲突，以抓包为准。

## 真正的根因方向：入场完成标记之后的尾巴被开关关掉了

`SelectCharacterDeliveryOptions.SendsFramesAfterTheCompletionMarker` 生产环境是 `false`，
其上的注释写着：「参考服务器的入场序列到 0x007C 就停了——53 个模板，后面什么都没有……
**客户端就是在读这段尾巴时死的**」。

**这份 A21 抓包推翻了它**：A21 在 `0x007C` 之后还有 **12 帧**。那句话是 A12 的事实。
而 `BuildPrePublicationMessages` 里现成实现的四条，正是参考尾巴里的四条：

| 已实现的消息 | 线上 opcode | 参考字节 |
|---|---|---|
| `UserVisibilityMessage` | NOTI `0x0165` | `01 00 03` |
| `CloneTitleStateMessage` | CMD `0x0239` | `01 00 00 00 00` |
| `AccountRosterStateMessage` | NOTI `0x0002` 子类型 `0x02` | 103 字节 |
| `HonorProgressStateMessage` | NOTI `0x0289` | `01 00 00 00 00 00 00 00` |

生产注册已改为 `Everything`，那段被推翻的注释按实测重写，`ReferenceSequence` 的摘要改注明它是
**A12** 的序列。四条尾帧已确认上线。

## USERINFO 子类型 `0x02` 就是花名册载荷

把参考那 103 字节按下标摊开后，`[0:18]` 与 `AccountCharacterRosterPayloadCodec` 的头部逐字段
一致（`0x02`、SlotLimit 32 两遍、`00`、i32 0、u16 0、i32 0、u16 条数），之后是一条角色条目：
槽位 u16、名字带长度、`00 00`、Job、GrowType、Level、`0x0B`、`0x01`、u32 0、u32 0、
外观件数、外观件×23、尾部 36（结尾 `00*6 FF 00*8`）。44 + 23 + 36 = 103，严丝合缝。

即：**进城时客户端用这条记录建自己的城镇形象**，而它复用花名册的编码。

## 建号不写外观，进城就没有形象可建

本仓角色 `appearance_blob` 长度为 0——`CreateAsync` 不写它（spec-002 已查明）。选人界面零外观
能渲染，但子类型 `0x02` 因此只有 82 字节（参考 103）。种入一件参考外观（`Slot=12,
DisplayItemId=0x06CB8080, ExpansionLength=4`，其余零）后变成 105 字节（名字 "Nagisa" 比 "1122"
长 2），结构与参考一致，角色在选人界面照常渲染。

**这处是必需的修正，但仍不足以止崩。** 正式做法是让建号写入外观，而不是像现在这样手工种；
这条尚未落地，当前库里那件是探针数据，收尾时要么实现建号写入、要么清掉。

## 已补齐的六个缺帧

参考发而我们完全不发的，按参考位置补入（字节全部取自抓包）：

| opcode | A21 名 | 字节 | 位置 |
|---|---|---|---|
| NOTI `0x0003` | USER_STATE | `01 01 00 00` | 子类型 1 之后、`0x0005` 之前 |
| NOTI `0x00CA` | VILLAGE_OBJECT_LIST | `00` | 同上 |
| NOTI `0x0465` | EPIC_BOOK_INFO | `00`×5 | 物品表之后、`0x0286` 之前 |
| NOTI `0x021E` | SET_USABLE_COUNT_INFO | `00` | 同上 |
| NOTI `0x0009` | PARTY_INFO | `00 00` | 完成标记之前最后一帧 |
| NOTI `0x000E` | UPDATE_ITEM_LIST | `00 00 00`（空表） | USER_POSITION 之后 |

六条已确认上线。

## 崩溃的真正性质：客户端是主动自杀的，而且原因不是协议解析

反汇编把这件事彻底定了性。故障指令是

```
0x01547BB0  c7 05 00 00 00 00 00 00 00 00   mov dword ptr [0], 0
0x01547BBA  c3                              ret
```

两条指令、前后 `int3` 填充——**一个故意的崩溃桩**。全镜像只有一个调用点（RVA `0x01239AD3`），判据是

```
cmp esi, 0x8000005B      ; 特定状态码
jne  → 通用错误分支（bl 保持 0，不自杀）
     → push L"InitGraphicSystem" … mov bl,1
cmp bl,1 / jne skip
call 崩溃桩
```

即：**`InitGraphicSystem` 返回 `0x8000005B` → 弹错误 → 按设计终止进程。** 这解释了为什么前五轮
协议修改一个字节都没让崩溃地址动过——它压根不是解析崩溃。

而参考抓包证明这台机器的图形没问题：SELECT_CHARACTER 之后还有 **402 帧**，有移动、心跳、
城内内容，最后 `0x0003` EXIT 干净登出。所以是我们喂的数据让它建不出人物。

## 建号不给装备，客户端就建不出人物模型

参考角色在槽位 12/14/16 穿着三件装备（子类型 1 里三条 60 字节条目，id `0x06CB8080` /
`0x28A0` / `0x3070`），外观是这三件的镜像。本仓角色**零装备、零外观**——`CreateAsync` 两样都不写。

按参考种入三件装备（`items.list_type=3`，Equipment）和对应的三件外观之后，
**客户端第一次成功进入游戏世界**：子类型 1 从 173 涨到 395 字节，城镇建成，
客户端发出 `CMD/0023 SET_USER_POSITION`——参考序列里客户端建好城镇后做的第一件事。

崩溃地址随之彻底改变，`InitGraphicSystem` 那条路径不再触发。

**正式做法是让建号写入初始装备与外观**，当前库里那三件加外观是探针数据，收尾时必须落地或清掉。

## 入场批次的顺序也按参考重排了

参考先发角色记录（USERINFO 子类型 0），**再**发物品表；本仓原本反过来——五个物品表在前，
角色记录在后，物品挂在一个还没建立的角色上。整个批次已按参考 67 帧逐帧重排。

同时撤掉三个**参考在入场序列里不发**的帧：`NOTI 0x0025`（客户端读它时把 66 万个元素拷出自己
栈外）、`NOTI 0x019F`（参考是在客户端请求后才答）、`NOTI 0x019D`。三个编码器都保留，只撤回
入场时的主动下发。

## 建号不写创建时间

SELECT_CHARACTER 应答 261 字节里两边只差 4 个字节：偏移 5..8，参考是 `6D CD 89 6A`
（Unix 时间戳），我们全零。根因是 `characters` 表**没有 `created_at` 列**，
`CharacterRosterService` 里硬编码 `DateTimeOffset.UnixEpoch`。

已补：建表加列、建号写 `unixepoch()`、两个查询读出、`CharacterSummaryRow` 加字段。
擦库重建后线上已是真实时间戳，应答与参考逐字节一致。（这一处是真 bug，但不是崩溃原因。）

## 当前卡点

客户端进城后发 `CMD/0023 SET_USER_POSITION`，**我们不应答**——`protocol/catalog.md` 把它记为
「Report / 无响应」，那是 A12 的行为；**A21 参考会回一帧 `NOTI 0x0016` USER_POSITION**（参考帧 71）。
这是入场后第一个分歧点。

之后客户端确定性地崩在 `0x02861C57`，写 `abs:0x001B0000`（`esp=0x001AF2C0`、`edi=0x001B0000`、
`ecx=0x000A252E` 每次一模一样）——栈缓冲区被超长拷贝写爆，说明还有一处长度字段不对。
栈上 `0x053526CF` 落在 `.vmp1` 段，那部分处理器是虚拟化的。

参考在这一步之后的对话（可直接照做）：

```
70 C->S 0x0023          → 71 S->C 0x0016 (9)
72,73 C->S 0x00C5 SAVE_GAME_OPTION
74 C->S 0x000F          → 75 S->C 0x0002(519) + 76 S->C 0x000F(1) + 77 0x0003(4) + 78 0x001A(1)
80 C->S 0x0010 (15)
81 C->S 0x008F (6)      → 87 S->C 0x008F (2)
   S->C 0x001B(37) 0x019F(2) 0x001C(32) 0x001D(23)
93 C->S 0x0025 FINISH_LOADING (8) → 94 S->C 0x001E (5)
```

## `SET_USER_POSITION` 的 A21 应答已实现

`protocol/catalog.md` 把 CMD `0x0023` 记为「Report / 无响应」，依据是 2026-08-17 那次 A12 抓包
（九帧一模一样、没有回包）。**A21 会答**：参考帧 70→71 相邻，`NOTI 0x0016` 九字节 =
`u16 线上用户 id` + 客户端刚上报的 x、y、方向和那个尾部 `u16`（参考里上报 `68 00`、回包也是
`68 00`，而入场帧是 `64 00`，所以尾部是客户端说了算而不是我们的常量）。

已实现：`SetUserPositionRequest` 增加 `Trailer`、解码器读第 5..7 字节、新增
`UserPositionNotification` + 编码器并在 `ProtocolModule` 注册、处理器改为回帧。
线上已确认 `C->S CMD/0023 body=7` → `S->C NOTI/0016 body=9`。

## 当前状态与下一步

客户端**能进入游戏世界**（可复现），拿到位置应答，但约一分钟内退出，且期间不再发任何帧。
参考在这一步之后客户端会继续发 `0x00C5 SAVE_GAME_OPTION` ×2、`0x000F`、`0x0010`、`0x008F`、
`0x0025 FINISH_LOADING`，我们一个都没等到。

`clienthack-faults.log` 里那条 `0x02861C57` 写 `0x001B0000` 是 **first-chance**，且
`esp=0x001AF2C0` 紧贴目标地址——**这是栈向下增长命中保护页的正常形态**，Windows 会自行处理，
不应据此断定为崩溃原因。真实死因尚未确定。

下一步按参考补齐入城后的一问一答（见上一节的帧序表），首要是 `0x000F` 和
`0x0025 FINISH_LOADING → NOTI 0x001E`。

## 建号写外观：探针数据已落地

判据收窄了一步：把三件装备删掉、**只留外观**，客户端照样进城。所以客户端要的是「有东西可以
建模型」，不是一套自洽的库存；装备缺失只让子类型 1 变短，不挡进城。

因此修复留在角色切片内（`appearance_blob` 就是 `characters` 的列），没有让角色反向依赖库存切片：
`CharacterRosterService.CreatedAppearance` 是三件转抄自参照会话的外观，
`ICharacterTable.InsertAsync` 增加一个 `appearanceBlob` 参数写进同一条 INSERT。

档案里查不到按职业的建号装备表——`character/<job>.chr` 的 `[initial value]` 只有属性和
`[skill]`，这个游戏里起始装备是服务端数据。所以给的是**一份对所有职业相同**的默认外观，
长期看不对，但它是目前唯一有证据的形状，注释里写明了这一点。

**已验证：擦库后由服务端自己建号的角色，零手工数据，进入游戏世界。**

## 仍未完成

- **子类型 `0x00` 还差约 16 字节。** 已知的另一处 A21 变化是无宠物时的三元组：参考写
  `FFFFFFFF` + `dstr("没有宠物")` + `0xFF` 共 21 字节，本仓写 `0u` + `dstr(空)` + 存活字节共 9 字节。
  **注意本仓与参考的角色不同**（名字长度 6 对 4、外观 0 条对 1 条），做长度对账时必须把这些
  数据差异先扣掉，否则会得出错误的结论——我在中途算过一次就是这么算歪的。
- **子类型 `0x01` 的头已对齐**（见下），但整体仍短：本仓 173、参考 519。差额主要是装备条目
  （参考那个角色有 3 件，本仓的新角色一件都没有），属数据差异；是否还有布局差异未定。

## 子类型 `0x01` 的头：逐字段由实测确定

```
[0]      01              subtype
[1..2]   01 00           version
[3..17]  15 字节零块      ← A21 新增（与子类型 0 的 38 字节零块同类）
[18..19] 01 00           角色标识
[20..23] 00 00 00 00     经验
[24..27] 58 00 00 00     = 88   ← 本仓此前写 83
[0x34..0x55]             34 字节垫，**末尾 u16 = 5500**（此前全零）
[0x6A..0x6D]             Weight
[0x6E..0x71] 64 00 00 00 = 100  ← 此前此处是一字节的当前 HP/MP 百分比
[0x72] 00  [0x73] 附加装备槽位  [0x74] 00  [0x75] 装备件数
```

改完之后本仓与参考**前 28 字节逐字节相同**。四处结构差异合计 +20 字节。

## 客户端的表现

三处改完之后客户端仍在收完入场批次、回完 `INFORM_NOTICE_2ND`/`CERA`/`MERCENARY_INFO` 之后
发一条 `CMD 02B3` 并退出，且留下一手异常：

```
0xC0000005 写 abs:0x00000000，at RVA 0x01147BB0
stack: 0x00D7D48E / 0x0123786A / 0x0123C142 / 0x0234E334 / 0x01101E5E
```

**这是真崩溃而不是优雅断开**，而且**五处改动做完之后仍崩在同一地址**——`ENTER_GAMEWORLD_COMPLETE`、
子类型 `0x06`、子类型 `0x00` 的零块、子类型 `0x01` 的四处、以及八条重编号，都不是它的直接原因。

栈上的 `0x00D7D48E` 与协议文档记载的接收派发链（`0x00D8566E` / `0x00D89C36`）相邻，
说明崩在某个**接收处理器内部**。定位它需要在 A21 镜像上反汇编那五个 RVA，
这是与前面几轮不同性质的工作，尚未开始。

## 表征守卫抓到的三条

全量测试有三条失败，**都是既有的表征用例抓住了本次有意的改动**，不是缺陷：

- `SelectCharacterSupplemental_PvpDirectAndPublicationBodiesPreserveExplicitOrder`：入场批次顺序变了。
- `SelectCharacterPayload_ObservedConstantBodiesHaveExactLengths`：`ENTER_GAMEWORLD_COMPLETE` 长度变了。
- `AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`：整段 transcript 的黄金值。

改它们之前要逐条看清楚每条断言在证什么——按仓库既有做法，形状改变导致黄金值重写时，
必须证明**值多重集未变**，而不是直接接受新输出。第四条失败是 `Host.Tests` 的满载假红，隔离复跑即绿。

## 取证装置的状态

- `scratchpad/wireclient.py`：能自动完成「连接 → 登录 → 建号」，不必驱动 GUI。
- `scratchpad/run-rig.sh`：服务端 17001/20011 + 记录型中转 7001/10011，**已指向 A21 的 Script.pvf**。
- `scratchpad/gui.ps1` + `drive-client.ps1`：真实输入注入（该客户端忽略 PostMessage）。
- 测试账号里现有一个由服务端 `CREATE_CHARACTER` 建出的角色（名 Nagisa），六张入场必需表齐全。
  此前手工种入的三个残缺角色已清除。
