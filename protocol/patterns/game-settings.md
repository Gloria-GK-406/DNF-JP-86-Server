# 设置族：客户端设置的上传与下发

客户端把玩家设置分成四块，各有独立的上传类型；服务端另有三条下发类型。
这一族横跨 7 个 type、两个方向，**上下行不是一一对应的**，因此提升到 `patterns/`
统一记录，而不是拆散在各协议文档里重复解释。

RVA/VA 约定见 [game-frame](game-frame.md)；构包原语与接收链见
[game-frame](game-frame.md) 与 [game-receive](game-receive.md)。

> **2026-08-16：本族有实机样本了，此前的两条前提都不成立。**
>
> 旧文写"服务端只在选角色之后才发下行帧，因此抓包里从未出现过本族任何一条"。实际上
> `0x00AD` 与 `0x01C7` 在**登录阶段**就下发（角色列表出现之前），客户端随即用 `0x00C5`
> 把主选项原样回存。三条的样本见 [0x00C5](../game/client-to-server/0x00C5.md)
> 与 `../game/server-to-client/samples/`。
>
> 下面标注了证据等级的结论仍以静态分析为准；被样本证实或推翻的地方逐条注明。

## 成员一览

### 上行 C→S（全部是 CMD）

| Type | 构包点 RVA | body | 内容 | 证据等级 |
|---|---|---|---|---|
| `0x00C5` | `0x00AFCFCA`、`0x00B04449` | `u32(134) + 134B` | 主游戏选项 | **实机已证** |
| `0x00C6` | `0x00AFD00A`、`0x00AFE102`、`0x00AFE291`、`0x00AFE2D0` | `u32(198) + 198B` | 按键槽 | Confirmed |
| `0x0170` | `0x00AFD34A`(bank 0)、`0x00AFD386`(bank 1) | `u8 bankIndex + u32(300) + 300B` | 快速聊天 | Confirmed |
| `0x01C0` | `0x00AFD698`、`0x00AFDA4C`、`0x00AFDAD6` | `u32(512) + 512B` | 角色设置 | Confirmed |

### 下行 S→C（全部是 NOTI）

| Type | handler RVA | 是否读 body | 落点 | 证据等级 |
|---|---|---|---|---|
| `0x00AD` | `0x0090D3B0` | **否** | — | High（见[否证](#关键不对称游戏选项只上不下)；但见下方**存疑**） |
| `0x01C7` | `0x0090D400` | 是 | `ReadU8` → `this+0x234`（keyType） | Confirmed |
| `0x0187` | `0x008F64F0` | 是 | `ReadU32`（长度前缀） | Confirmed |

三条 handler 的注册点都在批函数 `0x009301C0` / `0x0090BD20` 里，而这两个批函数唯一的调用者
`0x00D8A010` 拿到的 `ecx` 来自 `0x00D8879B` 处的 `call 0x00D8A070`（GetRegistryB），
所以它们确实是 **NOTI 表**的注册——服务端把这三条发成 `command = 0x00` 是对的。

`0x0170` 与 `0x01C0` 在 S→C 方向也各有 handler，但那是**同号不同协议**，
与本族的上行含义无关，需另建记录——见 [catalog 的命名与身份](../catalog.md#命名与身份)。

## 客户端 settings 单例的缓冲区布局

单例取自 `RVA 0x00B03050`。四块上传缓冲区两两首尾相接，这是判定尺寸的依据：

| 偏移 | 大小 | 上传类型 | 相接校验 |
|---|---:|---|---|
| `+0x8C` | 134 | `0x00C5` | `0x8C + 134 = 0x112` |
| `+0x112` | 198 | `0x00C6` | — |
| `+0x234` | — | （`0x01C7` 下发写入的 keyType） | — |
| `+0x44C` | 300 | `0x0170` bank 0 | `0x44C + 300 = 0x578` |
| `+0x578` | 300 | `0x0170` bank 1 | — |

`0x00C6` 的 198 字节 = 99 个 `u16` 键位槽，与服务端 `EncodeHotkeys` 写 `slots.Count * 2`
自洽。`0x00C5` 的 134 字节 = 67 个 `u16`，把服务端那份移植来的默认值按 `u16` 拆开就能看出
是设置值（`1 / 3 / 70 / 25 / 1000` 这类），不是文本。

## `0x00AD` 是催传信号，不是下发

handler 全文只有 17 条指令，做三件事，**没有任何一次读取原语调用**：

```asm
0x0090D3B0  call 0x00B03050 / call 0x00B041F0   ; ① 校验 +0x8C，长度不对就重置并上传
0x0090D3BC  call 0x00B03050 / call 0x00AFCE70   ; ② 校验 +0x44C 与 +0x578 两个 bank
0x0090D3C8  cmp  byte [VA 0x31A12B8], 0
0x0090D3CF  je   ret                            ; ③ 全局开关；为 0 就到此为止
0x0090D3D1  …    call 0x00B01E60
0x0090D3EC  jmp  0x00AFCFB0                     ; ④ 尾跳 → 上传 C→S CMD 0x00C5
```

① 里的自愈逻辑同时给出了 134 这个尺寸的独立佐证：

```asm
lea  ebx, [edi+0x8C];  push ebx;  call 0x01C91FD0
cmp  eax, 0x86            ; 缓冲区尺寸和 134 比
jne  → call 0x01F03F80    ; 不对就重置成默认值
     → call 0x00AFCFB0    ; 并立刻上传
```

上传出口 `0x00AFCFB0` 全镜像有 **17 个调用者**，本 handler 只是其一——玩家在界面里改设置
也走同一条路。所以 `0x00C5` 是"设置变了就上传"的通用出口，`0x00AD` 只是让服务端能主动催一次。

## 关键不对称：游戏选项只上不下

用跨调用图的读取原语可达性扫描（深度 4，遇打包区记为盲点，脚本
[`data/readreach.py`](data/readreach.py)）：

| handler | 结果 |
|---|---|
| `0x0090D400` NOTI `0x01C7` 按键 | READS via `0x00AFCDB0` → `ReadU8` |
| `0x008F64F0` NOTI `0x0187` 角色设置 | READS via `0x00AFDB00` → `ReadU32` |
| `0x0090D3B0` NOTI `0x00AD` 游戏选项 | **走遍 104 个函数，零命中** |
| `0x008C7E20` CMD `0x0001` LOGIN（正对照） | READS via `ReadU8` ✔ |
| `0x00936740` NOTI `0x00C5`（正对照） | READS via `ReadDString` ✔ |

两个正对照都正确检出，所以否定结果不是工具失灵。

**盲点**：`0x00AD` 子树里有 11 个调用进了打包代码区（`RVA ≥ 0x02300000`），静态跟不下去。
因此这是**很强的否证，不是证明**，等级记为 High 而非 Confirmed。

结论：

| 设置 | 下行 | 上行 |
|---|---|---|
| 按键 | NOTI `0x01C7`（客户端**读**） | CMD `0x00C6` |
| 角色设置 | NOTI `0x0187`（客户端**读**） | CMD `0x01C0` |
| 游戏选项 | NOTI `0x00AD`（body 被丢弃） | CMD `0x00C5` |
| 快速聊天 | 未找到读取方 | CMD `0x0170`（带 bankIndex） |

**只有按键和角色设置是真双向；游戏选项是单向上行。**

### 存疑：实机行为与"body 被丢弃"并不矛盾，但也没证实它

2026-08-16 抓包里服务端确实发了 `0x00AD`（146 字节），客户端随后用 `0x00C5` 回存了
**同样的 134 字节 blob**。这有两种解释，样本分不开：

1. handler 真的不读 body，那 134 字节是客户端自己内存里本来就有的选项，回存只是常规保存；
2. handler 读了（读取路径在打包区里，扫描看不见），把 blob 收下后再原样存回。

能确定的只有一条：**客户端不认那 8 个尾字节**（我们发 146，它回 138），
所以它们要么属于别的字段，要么是多余的。

要分辨这两种解释：下发一个**被改动过**的 blob，看客户端回存的是改动后的还是它原有的。
这是一次实机差分就能定的事，比继续静态扫描便宜得多。

## 旧服代码交叉核对

旧服路由表（提交 `1754c3d`，`Network/Protocol/GameProtocolHandler.cs`）：

```csharp
d[0x00C5] = Handle_SAVE_GAME_OPTION_1;    // SaveMainOption(accountId, blob)
d[0x00C6] = Handle_SAVE_GAME_OPTION_2;    // SaveHotkeySlots(accountId, blob)
d[0x0170] = Handle_SAVE_QUICKCHAT;        // body[0]=bankIndex, 再 u32 len + blob
d[0x01C0] = Handle_SAVE_CHARACTER_OPTION; // 角色级，不是账号级
```

这四条与客户端普查在 settings 模块（`RVA 0x00AF0000–0x00B10000`）里找到的构包类型**完全重合**，
两边互不知情——这是本页可信度最高的一条交叉证据。`0x0170` 的 `bankIndex` 前置字节更是
双向吻合：旧服读 `body[0]`，客户端构包点前一步正是 `WriteU8(0)` / `WriteU8(1)`。

旧服下发侧：`GameOptionBodyBuilder` 用 NOTI `0x00AD` 发 `main + bank0 + bank1` 三个 blob；
`CharacterOptionBodyBuilder` 用 NOTI `0x0187`。

**旧代码只有参考价值。** 它给出的**名字**站住了，给出的**因果**被上一节的测量推翻——
详见[逆向工作日志](#逆向工作日志)。

## 服务端实现映射与现状

| 类型 | 服务端 | 状态 |
|---|---|---|
| NOTI `0x00AD` | `AccountGameOptionsMessage`，body 写三个 blob | 发了，但客户端不读 |
| NOTI `0x01C7` | `HotkeyStateMessage` | 发了，客户端读 |
| NOTI `0x0187` | 未实现 | — |
| CMD `0x00C5` / `0x00C6` / `0x0170` / `0x01C0` | **均无 decoder** | 玩家改的设置上传后直接丢弃 |

编码器在
[`StateSynchronizationPayloadCodec.Entry.cs`](../../Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs)，
消息类型在
[`StateSynchronizationMessages.cs`](../../Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs)，
两条下发都在
[`SelectedCharacterStateMessageFactory.cs`](../../Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectedCharacterStateMessageFactory.cs)
第 55–56 行加入选角色状态批次。

### 下发时机（**分歧已判定：旧服是对的**）

旧服作者曾特意把 `0x00AD` / `0x01C7` 从选角色阶段挪到**登录之后**（提交 `1034904`），
理由写在 `AccountSettingsPacketBuilder` 的注释里。重构后的实现又把两条放回了选角色批次。
本页此前只记录分歧、不改代码，等一次能判定的实机证据。

**2026-08-16 证据到了，而且判得很干脆。**把上游服务端（`gitgud.io/rewio/86JP`，HEAD
`ebec883`）跑起来、用同一个客户端连上去，**建角色 UI 打得开**；连我们的服务端则打不开。
两边逐条比对，登录阶段唯一的差别就是这两个包：

```csharp
// 上游 LoginHandler.SendAccountSettingsOnLoginAsync，在 LOGIN 响应之后立即发
GamePacketEnvelopeBuilder.Build(0x00, 0x00AD, BuildGameOptionBody(main, quick0, quick1)),
GamePacketEnvelopeBuilder.Build(0x00, 0x01C7, BuildHotkeyOptionBody(keyType, accountHotkeys)),
```

旧服注释里的原话是「**必须在登录成功后、GET_USERINFO/选角前下发**」。它没说错，只是低估了
后果：不是"UI 勾选恢复了但运行态没应用"，而是**角色列表屏根本不让你进创建角色**。

所以这不是风格之争，是功能依赖。当前实现已把两条改为**登录响应之后立即下发**，
选角色批次里的那两条保留不动（选完角色后带上角色级热键，是另一件事）。

登录期的 `0x01C7` 只带**账号级**热键槽：那时还没选角色，角色级槽位不存在。旧服的
`AccountScopedHotkeySlotCount = 1`，与我们 `AccountSettingsEntryState.AccountHotkeySlot`
是单个 `ushort?` 正好对上，不是巧合——两边描述的是同一个槽。

**方法上的教训**：这条分歧当初被判定为"证据不足，只记录不改"，事后看是对的决定——
真去猜的话，猜中也是运气。真正解决它的不是更多静态分析，而是**把已知能工作的旧服跑起来做对照**。
一份可运行的参照实现，抵得上一堆推断。

## 未解问题

| 未解问题 | 影响 | 最便宜的证据 |
|---|---|---|
| 快速聊天有没有下发通道 | 换机器能否恢复 bank | 找写入 `+0x44C`/`+0x578` 的 handler |
| `0x00AD` 那 11 个打包区调用是否读流 | High 能否升到 Confirmed | 断在 handler 入口，比对流游标前后 |
| 全局开关 `VA 0x31A12B8` 的含义 | 决定 `0x00AD` 何时真会触发上传 | 找它的写入方 |
| `0x01C0` 那 512 字节的内部布局 | 服务端要不要理解内容 | 抓一个真样本 |
| 服务端只存档还是要解析内容 | 影响服务端建模深度 | 同上 |
| `0x00C6` 的 99 个 `u16` 与键位的对应 | 按键设置能否正确回放 | 改一个键后抓样本比对 |

## 逆向工作日志

### 1. 旧代码的名字可信，因果不可信

旧服代码是极好的**命名**来源——`SAVE_GAME_OPTION_1` 这类名字带着旧服枚举的血统，
而且这次四个类型和客户端普查完全重合，等于两条独立证据互证。

但同一份代码里的**因果陈述**必须当成待验假设。`AccountSettingsPacketBuilder` 的注释写着
"只在选择角色初始化阶段下发时，客户端会恢复 UI 勾选，但很多不会实际应用"，读起来像是
客户端消费了 `0x00AD` 的 body。测量结果是：同一提交里改的两个包，`0x01C7`（键盘）**真读**，
`0x00AD`（游戏选项）**根本不读**。作者把两个包的效果混在了一起，又把改善归给了时机改动。

**教训**：历史代码给"叫什么"，不给"为什么"。凡是"客户端会……"这类行为断言，都要自己测。

### 2. 否证必须带正对照

"扫遍 104 个函数没找到读取原语"这句话，单独说没有说服力——可能是扫描器坏了、
深度不够、或者调用图构造有 bug。所以同一次扫描里放了两个**已知会读**的 handler
（LOGIN `0x0001` 与 NOTI `0x00C5`）当正对照，两个都正确检出，否证才立得住。

同理，盲点要主动报出来：脚本会把跳进打包区的调用单独列出来，所以"11 个盲点"是结论的一部分，
不是被掩盖的例外。这也是本条只记 High 不记 Confirmed 的原因。

### 3. 缓冲区首尾相接是最好的尺寸校验

`+0x8C + 134 = +0x112`、`+0x44C + 300 = +0x578`——两对缓冲区严丝合缝顶到下一个字段。
单看一个 `push 0x86` 只知道"这次写了 134 字节"，可能是截断也可能是子集；
而相邻字段的偏移差恰好等于长度，说明 134 就是这块缓冲区的**全长**。

这个技巧在 [`0x0001`](../game/server-to-client/CMD-0x0001.md) 判定 host 数组长度时也用过
（`0x94C + 260×2 = 0xB54`），可以当成通用手法：**先找相邻成员，用偏移差反推数组上界**。
