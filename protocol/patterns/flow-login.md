# 登录到选人界面：谁先说话、谁在等、名单从哪来

从 TCP 连接建立到客户端停在角色列表屏，是一条**由服务端先开口、随后在四个客户端 handler
里各自成批发帧**串起来的流程。它和[进入世界](world-entry.md)的结构不同：进城那一段是"一大批
下行帧 + 一个闸点"，登录这一段是一串**请求／响应接力**——每一批 C→S 都由上一批 S→C 里
某一帧的 handler 发出，四个接力点分别是 NOTI `0x0001`、CMD `0x0001`、NOTI `0x00AD`、
NOTI `0x0002` 的 handler。

本页记录这个编排。单条协议的字节布局见各自文档；帧头、CMD 两字节结果信封、CMD/NOTI 两张
注册表、入站流与读取原语见 [game-frame](game-frame.md) 与 [game-receive](game-receive.md)，
本页不重复。

**范围**：TCP 连接 → 角色列表就绪 → 客户端在选人界面上发出的帧。
`CMD 0x0004 SELECT_CHARACTER` 及其之后属于 [world-entry.md](world-entry.md)。
建角色／删角色／查名属于 [character-lifecycle.md](character-lifecycle.md)。
设置族四条上行三条下发的**布局**属于 [game-settings.md](game-settings.md)。

## 全景时序

下面是 2026-08-17 最后一次实机会话（抓包 `clienthack-frame-capture.log` 行 1049–1069，
读取跟踪 [`data/traces/read-trace-20260817-entry-batch.log`](data/traces/read-trace-20260817-entry-batch.log)
第 1–13 帧）。两条标 `‹抓包不可见›` 的发送不走加密路径，抓包看不到它们，
但它们消耗了 seq 号，且服务端确实收到（见[两个仪器的盲区](#两个仪器的盲区与对账)）。

```text
── 频道端点（另一条 TCP，另一套帧与加密；本窗口内无抓包）────────────────
C→S  Channel  0x05  脚本版本      S→C  0x06
C→S  Channel  0x09  脚本本体      S→C  0x0A   channel_info.etc
C→S  Channel  0x0B  连接          S→C  0x0C   36B，含日密钥（ASCII，偏移 4）
C→S  Channel  0x11  频道表        S→C  0x12   每频道 48B：name/max/cur/host/port
                                             ↑ 客户端由此得到要拨的 (host, port)

── 游戏端点（下面全部有抓包 + 读取跟踪）─────────────────────────────────
                     TCP connect
S→C  NOTI 0x0001  ‹两个仪器都不可见›  初始连接通知（channelId、时戳、host、
                                      CommandPacketCount/NotificationPacketCount）
C→S  CMD  0x0001  seq 0  body 73   LOGIN            ← 由上面那条 NOTI 的 handler 发出
S→C  CMD  0x0001         body 53   LOGIN 响应（ok=1）
     ┌ 以下四条全部在 CMD 0x0001 响应的同一个 handler 里发出，中间没有任何入站帧 ┐
C→S  CMD  0x00AB  seq 1  body 39   CLIENT_SPEC_STATISTIC
C→S  CMD  0x01DD  seq 2  body 16   COMPATIBILITY_INDEX
C→S  CMD  0x0008  seq 3  body 3    GET_USERINFO   FF FF 02  ← 拉名单的就是这一条
C→S  CMD  0x0286  seq 4  body 0    ‹抓包不可见›  CHARAC_VIEW_HIDDEN_CHARAC_INFO
     └──────────────────────────────────────────────────────────────────────┘
S→C  NOTI 0x00AD         body 146  账号游戏选项  ┐
C→S  CMD  0x00C5  seq 5  body 138  ← 由 0x00AD 的 handler 立刻回存    │ 六条
S→C  NOTI 0x01C7         body 7    账号级热键（keyType + 1 个 u16 槽）│ 登录期
S→C  NOTI 0x00B7         body 2    拍卖服务状态 00 01                 │ 账号
S→C  NOTI 0x00B7         body 2    拍卖服务状态 01 01                 │ 设置
S→C  NOTI 0x0289         body 8    HONOR_LEVEL_INFO                   │ 批次
S→C  NOTI 0x01A1         body 1    公告通知（1 个零字节）            ┘
S→C  NOTI 0x0002         body 134  **角色名单**，subtype 2，count = 2  ┐ GET_USERINFO
S→C  CMD  0x0286         body 2    00 04（拒绝，服务端主动推）        │ 的回答
S→C  NOTI 0x0289         body 8    HONOR_LEVEL_INFO（第二次）        ┘
C→S  CMD  0x01BA  seq 6  body 0    ‹抓包不可见›  ← 由名单 handler 尾调用发出
S→C  CMD  0x01BA         body 7    01 + 空佣兵名单
                     ── 角色列表屏就绪，等玩家操作 ──
C→S  CMD  0x02C1  seq 7  body 1    CONTENTS_PLAY_INFO  01
S→C  CMD  0x02C1         body 19
C→S  CMD  0x02C1  seq 8  body 1    01
S→C  CMD  0x02C1         body 19
C→S  CMD  0x0004  seq 9  body 2    SELECT_CHARACTER   → world-entry.md
```

两条不可见发送在上图里的位置**是按 seq 推定的**（`0x0286` 夹在 seq 3 与 seq 5 之间、
`0x01BA` 在 seq 5 之后），它们与相邻入站帧的精确交错顺序抓包给不出。它们各自由哪个 handler
发出则是确定的，见第三节与第五节。

四个接力点的边界很清楚：**每一批 C→S 都由上一批 S→C 里某一帧的 handler 发出**。
下面逐段给出这个因果链的指令级证据。

## 一、两个端点：频道是目录，游戏是会话

频道端点和游戏端点是**两条独立的 TCP 连接**，帧格式、消息编号空间和加密都不共享：

| | 频道端点 | 游戏端点 |
|---|---|---|
| 消息编号 | `0x05` / `0x09` / `0x0B` / `0x11`（单字节） | 13 字节头里的 `u16 type` + command 分族 |
| 响应编号 | 恒为 `请求编号 + 1`（`0x06`/`0x0A`/`0x0C`/`0x12`） | 同 type 回 CMD，或推 NOTI |
| 加密 | 按日密钥 `yyyyMMdd000006`，部分响应还压缩 | `Cipher::Encrypt`/`Decrypt`，NOTI `0x0001` 例外 |
| 本页范围内的作用 | **目录 + 脚本 + 密钥引导**，此后不再使用 | 全部业务 |

**衔接点是端口。** 频道表的每个条目 48 字节，含 `host(16)` 与 `port(4)`；客户端据此拨号，
而服务端反过来用**监听端口**识别这是哪个频道，并把结果写进初始连接通知里的 `ch.<id>`
与 channelId 字节。

- 频道表编码与日密钥：[`ChannelInfoResponseEncoder.cs`](../../Server/src/Dfo.Server.Protocol/Channel/Info/ChannelInfoResponseEncoder.cs)、
  [`ChannelProtocolKeyProvider.cs`](../../Server/src/Dfo.Server.Protocol/Channel/ChannelProtocolKeyProvider.cs)。
- 端口 → channelId：[`ChannelEntryPolicy.ResolveAsync`](../../Server/src/Dfo.Server.Application/Channel/ChannelEntryPolicy.cs)
  按 `channel.Port == listenerPort` 查同一份频道目录；
  [`OnlineUserConnectionObserver.ConnectedAsync`](../../Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs)
  再把它交给初始连接通知。

**这一段没有抓包。** 抓包插件挂在游戏端点的 `Cipher::Encrypt`/`Decrypt` 上，频道端点用另一套
加密，因此频道握手的**顺序、次数、以及客户端是否重连**目前只有服务端的 decoder/encoder 作依据，
没有客户端侧证据。这是本页最大的证据空洞。

### LOGIN 响应里那组端点不是这两个之一

`CMD 0x0001` 响应带 `host + portA + portB`，实测 `"127.0.0.1"` / 17200 / 17200。
它配的是**公告/消息端点**，是第三个东西；而且 2026-08-16 的裸监听实测证明
**从登录到角色列表全程客户端连它连 SYN 都没有**。见
[CMD-0x0001.md 的 `E-007`](../game/server-to-client/CMD-0x0001.md#verification)。
所以本流程内不必实现它。

## 二、服务端先说话：`NOTI 0x0001` 与它的加密豁免

### 客户端的 LOGIN 是一条 NOTI 的 handler 发出来的

抓包给出 LOGIN 帧的调用栈（会话末尾那次，`clienthack-frame-capture.log` 行 1049）：

```text
01C9007C  00D8BE57  0091584A  00D8577A  00D89CFC  01C90D3B  00C601BA  …
                    ↑ handler  ↑ NOTI    ↑ 接收函数里 call NOTI 分发器
                               分发器内   的返回地址
```

后两帧把它钉死在 **NOTI 分发路径**上（由客户端 `F78F0AEA…C04C` 的
`0x00D89CE9`–`0x00D89CFF` 与 `0x00D85770`–`0x00D8578F` 反汇编得出）：

```asm
0x00D89CF0  mov  dword ptr [ebp-4], 1
0x00D89CF7  call 0xd856f0        ; NOTI 分发器（表 B），返回地址 0x00D89CFC ✔
...
0x00D85778  call edx             ; 分发器调用 handler，返回地址 0x00D8577A ✔
```

第三帧 `0x0091584A` 落在 handler 内部（由 `0x00915830`–`0x0091585A` 反汇编得出）：

```asm
0x00915845  call 0xd8ba10        ; ← 返回地址 0x0091584A；0x00D8BA10 内含
0x0091584A  lea  edx, [ebp-0x29d]  ;   C→S 0x0001 的唯一构包点 0x00D8BA3A
0x00915850  push 1
0x0091585A  call 0x1d00420       ; ReadU8 —— 发完 LOGIN 才继续读自己的 body
```

包含 `0x0091584A` 的已注册 NOTI handler 只有一个：**入口 `0x00914A50`，即 NOTI `0x0001`**
（普查 [protocol-identity.tsv](data/protocol-identity.tsv)；该区间内相邻的 NOTI handler 入口是
`0x00914A50` 与 `0x00915B40`，`0x0091584A` 落在前者内，且 `0x00914A50` 是干净的
`push ebp; mov ebp,esp` + SEH prologue）。

**观察到的是**：LOGIN 由一个 NOTI handler 发出，该 handler 是 NOTI `0x0001` 的。
**由此推论**（服务端侧独立佐证）：我们的服务端在每条游戏连接建立时就推 NOTI `0x0001`
（[`OnlineUserConnectionObserver.ConnectedAsync`](../../Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs)），
所以**是服务端先说话，客户端的 LOGIN 是对它的回应**——不是客户端主动发起。

### 只有 `(command=0x00, type=0x0001)` 不走 `Cipher::Decrypt`

这解释了为什么两个仪器都看不见它。接收函数里有一道按 header 前三字节的分叉
（由客户端 `F78F0AEA…C04C` 的 `0x00D89950`–`0x00D899C4` 反汇编得出）：

```asm
0x00D89950  cmp   byte ptr [ebx], 0      ; header[0] = command
0x00D89953  movzx edx, word ptr [ebx+1]  ; header[1..2] = type
0x00D8995A  jne   0xd899c9               ; command != 0x00 → 解密路径
0x00D8995C  cmp   edx, 1
0x00D8995F  jne   0xd899c9               ; type != 0x0001 → 解密路径
            ...                          ; 落到这里的只有 (0x00, 0x0001)
0x00D8999B  call  0x18727e0              ; 固定密钥 XOR
0x00D899C4  jmp   0xd89aa2               ; → SetRemaining，**从不调用 Cipher::Decrypt**
```

而解密路径在 `0x00D89A36` 调用 `0x01C11420`（返回地址 `0x00D89A3B`，正是每条入站帧记录到的
那个恒定栈顶，见 [game-receive](game-receive.md#为什么调用栈在-sc-上不管用)）。

`0x00D8999B` 那五个字节 `E8 40 8E AE 00` 正是 ClientHack 的 `disable-fixed-key-xor`
补丁位置（[`network_hook.c` 的 `apply_network_xor_patch`](../../ClientHack/plugins/0100_network_hook/network_hook.c)、
签名 `CH_ORIG_NETWORK_XOR_00D8999B`、地址台账
[`ClientHack/docs/address-provenance.md`](../../ClientHack/docs/address-provenance.md)）。
把它 NOP 掉之所以必要且足够，就是因为我们的服务端把初始连接通知**发成明文**。

两条推论，对服务端实现都有直接影响：

- **初始连接通知必须是明文**（在 XOR 被 NOP 的构建上），其余帧走正常路径；
- **`(0x00, 0x0001)` 这个组合被客户端接收层特判了**，不能拿它当普通 NOTI 复用。

## 三、LOGIN 响应的 handler 一口气发四条

`CMD 0x0001` 响应的 handler 是 `RVA 0x008C7E20`（普查 + [CMD-0x0001.md](../game/server-to-client/CMD-0x0001.md)）。
它内部**四个出站构包路径**的偏移与抓包 seq 严格同序递增：

| seq | type | 构包路径 | handler 内偏移 |
|---:|---|---|---|
| 1 | `0x00AB` | `0x008C7E7E: call 0x01CD12C0` → … → `0x01CD1134 MakeHeader(0x00AB)` | `+0x5E` |
| 2 | `0x01DD` | `0x008C7E89: call 0x01CD11C0` → `0x01CD11CA: call 0x00C375B0` → `0x00C375E1 MakeHeader(0x01DD)` | `+0x69` |
| 3 | `0x0008` | `0x008C86A6 MakeHeader(0x0008)`（内联） | `+0x886` |
| 4 | `0x0286` | `0x008C8A5A MakeHeader(0x0286)`（内联） | `+0xC3A` |

两个中间函数各**只有一个调用者**（全镜像扫 `E8 rel32`：`0x01CD12C0` 只被 `0x008C7E7E` 调用，
`0x00C375B0` 只被 `0x01CD11CA` 调用），所以这条链没有歧义。
`0x008C7E20`–`0x008C8C00` 之间**没有任何 `CC`/`90` 填充**，即四个点确实同属一个函数体。
（以上均由客户端 `F78F0AEA…C04C` 的 `0x008C7E20`–`0x008C8C00` 反汇编与全镜像 call 扫描得出；
构包点集合另见 [protocol-identity.tsv](data/protocol-identity.tsv)。）

### 整段被结果信封的 `ok` 一票否决

```asm
0x008C7E20  push ebp / mov ebp,esp / …          ; handler 入口
0x008C7E53  cmp  byte ptr [ebp+0x0C], 0         ; [ebp+0xC] = 信封 ok
0x008C7E57  je   0x008C8A6E                     ; ok == 0 → 跳过全部四条
0x008C7E7E  call 0x01CD12C0                     ; → 0x00AB
0x008C7E89  call 0x01CD11C0                     ; → 0x01DD
0x008C7EA9  call 0x1d00420                      ; ← 第一次读 body（offset 0）
```

两件事从这段代码直接读出来：

1. **登录被拒（`ok = 0`）时客户端一个字节都不发。** 信封语义见
   [game-receive](game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)。
2. **`0x00AB` 与 `0x01DD` 在 handler 读第一个字节之前就发出去了**——它们跟响应内容无关，
   纯粹是"登录成功了，把机器信息交上去"。这也解释了为什么这两条的 body 在多次会话之间
   逐字节一致。

### `0x0286` 属于 LOGIN 批次，不在名单之后

`0x0286` 的构包点在 `+0xC3A`，**在同一个 handler 内**，而且它是 seq 4——夹在 `0x0008`（seq 3）
与 `0x00C5`（seq 5）之间。`0x00C5` 是 `NOTI 0x00AD` 的 handler 发的（下一节），
而 `0x00AD` 是六条批次的第一条，比角色名单早 7 帧到达。所以 seq 4 必然在名单到达**之前**
就被消耗掉了。

这同时从客户端侧独立确认了 seq 4 的身份——此前只有服务端的"未识别入站帧"日志作依据
（[catalog](../catalog.md#抓包并不完整seq-4-与-seq-6已解决)）。

> `0x0286` 另有第二个构包点 `0x008C5D11`，落在 `CMD 0x0006 DELETE_CHARACTER` 响应的 handler
> `0x008C5C60` 内（`+0xB1`）——删角色之后客户端会再查一次。属
> [character-lifecycle](character-lifecycle.md) 的范围。

### 这四条之间客户端不等

seq 1–4 是连续四次发送，抓包里**中间没有任何入站帧**（入站有独立钩子，覆盖同一条解密路径，
所以"其间无入站帧"是可以断言的，见 [game-frame](game-frame.md#抓包的覆盖边界)）。
服务端不能假设"客户端发 `0x0008` 时已经收到了什么"。

## 四、账号设置批次：六条 NOTI，由 LOGIN handler 推

| # | 族 | type | body | 实测字节 | 内容 |
|---:|---|---|---:|---|---|
| 1 | NOTI | `0x00AD` | 146 | `u32(134) + 134B + 8 个零` | 账号游戏选项 |
| 2 | NOTI | `0x01C7` | 7 | `00` + `u32(2)` + `0200` | 账号级热键：keyType + 1 个 `u16` 槽 |
| 3 | NOTI | `0x00B7` | 2 | `00 01` | 拍卖服务 0 开放 |
| 4 | NOTI | `0x00B7` | 2 | `01 01` | 拍卖服务 1 开放 |
| 5 | NOTI | `0x0289` | 8 | `01000000 00000000` | 荣誉等级 |
| 6 | NOTI | `0x01A1` | 1 | `00` | 公告通知 |

发送方是
[`GameLoginHandler.SendAccountSettingsAsync`](../../Server/src/Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs)，
顺序与上游 `LoginHandler.cs:83-91` 一致（上游把它拆成
`SendAccountSettingsOnLoginAsync` 的两条 + `AuctionServiceNotificationBuilder.BuildOpenPackets()`
两条 + 荣誉 + `0x01A1`）。

### 这个顺序是服务端定的，客户端给不出证据

抓包的顺序**不能**用来判断这六条是 LOGIN 触发的还是 GET_USERINFO 触发的：客户端在它们到达
之前就已经把 `0x0008` 发出去了。能定这件事的只有服务端代码（我们的和上游的），两边都放在
LOGIN 的 handler 里。

### `0x00AD` → `0x00C5` 是因果，不是顺序

`0x00C5`（seq 5）的抓包调用栈：

```text
01C9007C  00B04465  0090D3BC  00D89CFC  01C90D3B  …
          ↑ 0x00B041F0 内   ↑ NOTI 0x00AD 的 handler（入口 0x0090D3B0）
```

`0x0090D3B0` 正是 `NOTI 0x00AD` 的 handler，`0x00B041F0` 是它调用的选项缓冲区校验函数，
而 `0x00B04449` 是 `0x00C5` 的构包点之一——三者与
[game-settings 的 `0x00AD` 反汇编](game-settings.md#0x00ad-是催传信号不是下发)完全吻合。
所以 **`0x00AD` 是催传信号，`0x00C5` 是它催出来的回存**，这条链现在有运行时栈作证。

顺带一条对账：读取跟踪记录 `NOTI 0x01C7` 的第一个读取者是 `RVA 0x00AFCDC2`，
而 game-settings 静态得到的是 handler `0x0090D400` → `0x00AFCDB0` → `ReadU8`——
`0x00AFCDC2` 就在 `0x00AFCDB0` 里面。静态与运行时在这条上互相印证。

### 这批帧是必需的，但"必需"的准确说法只有一条

已被实机判定的是：**这六条放在登录期，建角色 UI 能打开；只放在选角色批次里，打不开**
（2026-08-16 与上游服务端对照，见
[game-settings 的下发时机](game-settings.md#下发时机分歧已判定旧服是对的)与
[character-lifecycle 的工作日志第 3 条](character-lifecycle.md#逆向工作日志)）。

**尚未被判定的**有两件，不要把它们当成已知：

- 六条里**哪几条**是承重的（没有逐条删除重试过）；
- 它们是否必须排在**角色名单之前**。客户端并不等它们，所以"在 GET_USERINFO 之前"这句
  上游注释里的话，在 wire 上只能理解成"在名单帧之前"，而这一点没有单独验证过。

## 五、`GET_USERINFO` 才是把名单拉出来的那一条

**角色名单不是服务端主动推的。** 三条独立证据：

1. **客户端侧（指令级）**：`0x0008` 的 `mode=2` 构包点在 LOGIN 响应 handler 内
   （`+0x886`，见上表），body 恒为 `FFFF 02`——`0xFFFF` 是"无目标"哨兵，`mode=2` 是账号级查询。
   全部 5 个构包点与哨兵的范围门见 [0x0008.md](../game/client-to-server/0x0008.md)。
2. **服务端侧**：`NOTI 0x0002` subtype 2 在本服务端只有两个发送点——
   [`GetAccountCharacterRosterHandler`](../../Server/src/Dfo.Server.Protocol/Game/AccountRoster/GetAccountCharacterRosterHandler.cs)
   （回答 `0x0008`，`AnswersRosterQuery: true`）和
   [`CreateCharacterSlice`](../../Server/src/Dfo.Server.Protocol/Game/CreateCharacter/CreateCharacterSlice.cs)
   （建角色后补发，`AnswersRosterQuery: false`）。上游同构：
   `CharacterSelectHandler.Handle_ENUM_CMDPACKET_GET_USERINFO`（`d51e74b`，`:560`）。
3. **wire 侧**：12 次会话里名单帧**一次都没有**出现在 `0x0008` 之前。

### 回答由三帧组成，顺序固定

```text
S→C  NOTI 0x0002  subtype 2   角色名单（body 长度随角色数变化）
S→C  CMD  0x0286  00 04       服务端**主动推**的拒绝，不是对 seq 4 那条请求的应答
S→C  NOTI 0x0289  荣誉等级    本会话第二次
```

`CMD 0x0286` 这一帧值得单独说：客户端在 seq 4 确实请求过它，但本服务端**故意不应答那条请求**，
而是跟名单一起推一次；上游连 inbound handler 都没注册，同样是推。所以一次会话里这条 CMD
只出现一次。理由写在
[`HiddenCharacterInfoSlice.cs`](../../Server/src/Dfo.Server.Protocol/Game/HiddenCharacterInfo/HiddenCharacterInfoSlice.cs)。

**与上游的一处差异**：上游在这三帧之后还发一条 `NOTI 0x0025`（账号经验进度，
`CharacterSelectHandler.cs:563` → `GrowthCapsuleSyncService.SendExpProgressAsync` →
`GamePacketEnvelopeBuilder.Build(0x00, 0x0025, …)`），我们不发。客户端在没有它的情况下
照常显示名单并能建角色，所以它不是到达选人界面的必要条件；但这条差异是实测存在的。

### 名单帧的 handler 自己又发两条

`NOTI 0x0002` 的 handler 入口 `0x00B56790`。它的 subtype 2 分支尾部（由客户端
`F78F0AEA…C04C` 的 `0x00B56920`–`0x00B569B2` 反汇编得出）：

```asm
0x00B56929  call 0x1d00490          ; ReadU16 → 角色数 count
0x00B56935  cmp  ax, word [ebp-8]
0x00B56939  jae  0xb5694e           ; count == 0 → 跳过 per-character 循环
0x00B56940  call 0xb562e0           ; ×count，逐个角色
0x00B5695C  call 0x1c3f4f0          ; ← 该函数 0x01C3F52C 把就绪标志 [esi+0x70] 置 1
0x00B569A1  call 0x1c332f0          ; ← 内含 C→S 0x02A8 的构包点 0x01C3331C
0x00B569AD  call 0xcdde80           ; ← C→S 0x01BA 的唯一构包器（4 条指令，body 0）
```

`0x00CDDE80` 全文就四条指令，body 长度 0：

```asm
0x00CDDE80  push 0x1ba
0x00CDDE85  call 0x1c8f5b0          ; GetWriter
0x00CDDE8A  mov  ecx, eax
0x00CDDE8C  call 0x1c90460          ; MakeHeader(0x01BA)
0x00CDDE91  jmp  0x1c8fdd0          ; Finalize
```

于是"**每一份 subtype 2 名单后面都跟一次 `0x01BA` 往返**"成了机制性结论，而不是巧合。
抓包对得上：12 次会话共 **17 帧** subtype 2 名单，其后 **17 次**都出现了
`CMD 0x01BA` 响应，无一例外；间隔为 3 帧（名单在 GET_USERINFO 批次里，中间夹着
`0x0286` 与 `0x0289`）或 1 帧（名单是建角色后的补发）。

这也把那条抓不到的 seq 定了下来：`0x01BA` 就是每次会话第二条不可见的发送
（普通会话 seq 6；`0x02A8` 出现的会话 seq 7）。见
[两个仪器的盲区](#两个仪器的盲区与对账)。

## 六、停在选人界面：客户端还会发什么

### `0x02C1 CONTENTS_PLAY_INFO`——严格一问一答，次数不定

抓包 12 次会话，`0x02C1` 的发送与响应**从不重叠**：`send / recv / send / recv …`，
一次都没有出现两次连续发送。这是"客户端等这条响应"的直接证据。

| 观察项 | 12 次会话的结果 |
|---|---|
| 往返次数（`0x0004` 之前） | 1、2、3、4、5 都出现过 |
| body 那一字节 | `00`、`01`、`02` 都出现过（`0x02C1.md` 此前只记了 `01`/`02`） |
| 调用栈 | `01C9007C 01C41066 01C41F8C 01C42xxx 01C4A896 01D15B9A …`——**没有任何分发器帧**，是 UI 层发出的 |
| 与随后 `0x0004` 请求 body 的关系 | 找不到——`0x0004` body `0100` 的两次会话分别发了 3 次和 2 次；body `0000` 的两次分别发了 5 次和 1 次 |

**观察是**：次数与取值都随会话变化，且不由任何入站帧触发。
**因此**服务端不能把它当成流程里的固定一步，也不能假设次数。响应恒为
`信封 01 + 18 个零`，客户端每次都继续往下走（[0x02C1.md](../game/client-to-server/0x02C1.md)）。

### `0x02A8 LOAD_EXTEND_CHARACS`——名单 handler 里的一个条件门

构包器 `0x01C332F0` 全文（由客户端 `F78F0AEA…C04C` 的 `0x01C332F0`–`0x01C33335` 反汇编得出）：

```asm
0x01C332F0  cmp   byte ptr [ecx+0x70], 0
0x01C332F5  movzx esi, word ptr [ecx+0x9c]
0x01C332FC  je    0x1c33334           ; 就绪标志为 0 → 不发
0x01C332FE  cmp   byte ptr [ecx+0xa0], 0
0x01C33305  jne   0x1c33334           ; +0xA0 非 0 → 不发
0x01C33307  cmp   esi, 0x24
0x01C3330A  jb    0x1c33334           ; +0x9C < 0x24 → 不发
0x01C3330C  push  0x2a8
0x01C33311  mov   byte ptr [ecx+0x70], 0   ; ← 发之前把就绪标志清零
            … MakeHeader(0x02A8) / WriteU16(esi) / Finalize
```

它由名单 handler 在 `0x00B569A1` 无条件调用（上一节），而**能不能发由那三个门决定**。
抓包侧的观察：

| 会话 | 首份名单 body | count | 是否出现 `0x02A8` |
|---|---:|---:|---|
| 2、10 | 18 | 0 | **出现**（seq 6，body `FFFF`） |
| 12 | 134 | 2 | 不出现 |
| 1、3–9、11 | 192 | 3 | 不出现 |

（样本账号的角色名都是 4 字节，此时 `18 + 58×count` 与四个观测长度精确闭合：18 / 76 / 134 / 192。
名字是变长 dstring，**58 不是常量**；per-character 布局属协议层，见
[NOTI-0x0002.md](../game/server-to-client/NOTI-0x0002.md)。）

**观察**：12 次会话里 `0x02A8` 只在名单为空的两次出现，且这两次正是玩家随后建角色的会话。
**推论边界**：门是读**选角屏对象的三个字段**（`+0x70`/`+0x9C`/`+0xA0`），不是读 count——
`0x00B569A1` 在 count 的两条分支汇合之后，两种 count 都会走到。
所以"名单为空 ⇒ 发 `0x02A8`"是相关，机制上是 count 通过某种方式影响了那三个字段之一，
**具体是哪一个未解**。

回包的作用（解锁选角屏 18 处交互）与 A/B 结论见
[0x02A8.md](../game/client-to-server/0x02A8.md)。本页新增的机制细节：
**发送自己会把就绪标志清零，所以回包是"重新置位"而不是"首次置位"**——名单帧在
`0x00B5695C` 已经置过一次了。

### `CMD 0x0002 SET_UDP_IP_PORT`——和本流程无序

12 次会话**每一次都出现**（`catalog` 记的"五次里出现四次"来自 2026-08-14/15 那批样本，
两批都成立），但**位置在选人界面前后随机**：

| 相对 `0x0004 SELECT_CHARACTER` | 会话 |
|---|---|
| 之前（还在选人界面） | 2、3、4、5、6、7、10 |
| 之后（已经进城） | 1、8、9、11、12 |

31 字节 body 在 12 次会话里**只有两个字节变化**（body 偏移 9–10：`2022`/`20A2`/`2062`/`20E2`/`18A2`），
其余 29 字节逐字节相同，含 `AC1EE001`（= 172.30.224.1）与 12 字节 ASCII 串 `30e3a448ed0f`。

**因此**：不要把它编排进登录流程。它是客户端 UDP 子系统自己完成端点探测后的一次上报，
落在哪一帧之间取决于探测什么时候完成。字段布局见
[0x0002.md](../game/client-to-server/0x0002.md)。

### 选人界面上的其它动作

- **建角色 / 删角色 / 查名**（`0x02B5` → `0x0005`、`0x0006`）：会话 2、10 里各建了 2–3 个角色。
  一问一答严格交替（`send 0x02B5 / recv 0x02B5 / send 0x0005 / recv 0x0005`），
  且每次成功都会**补发一份完整名单**（`AnswersRosterQuery: false`），从而再触发一次
  `0x01BA` 往返。族记录见 [character-lifecycle.md](character-lifecycle.md)。
- **`0x0004 SELECT_CHARACTER`**：调用栈同样是纯 UI 的
  （`01C3C309 01C3D246 01C43781 00D92D45 …`，无分发器帧），构包点 `0x01C3C2F2`。
  之后的一切见 [world-entry.md](world-entry.md)。

## 等与不等

| 客户端行为 | 帧 | 判据 |
|---|---|---|
| **等**，而且后续动作由响应 handler 发出 | `0x0001` LOGIN | 四条后续请求的发送路径都从 handler `0x008C7E20` 内出发（`+0x5E`/`+0x69`/`+0x886`/`+0xC3A`） |
| **等**，且响应决定要不要发后续 | `0x0001` LOGIN | `0x008C7E53` 的 `cmp ok,0 / je` 一票否决 |
| **等**，严格一问一答 | `0x02C1` | 12 次会话共 41 次往返（41 发 41 收），从无两次连续发送 |
| **等**，严格一问一答 | `0x02B5` / `0x0005` | 会话 2、10 的交替序列 |
| **等**，回包解锁 UI | `0x02A8` | 就绪标志被发送清零、由回包置位（本页 + `0x02A8.md`） |
| **不等**（同一 handler 内连发） | `0x00AB`、`0x01DD`、`0x0008`、`0x0286` | seq 1–4 之间抓包无入站帧 |
| **不等**（纯上报，无响应） | `0x00C5` | 12 次会话中服务端从未回帧，客户端照常前进 |
| **不等**（纯上报，位置随机） | `0x0002 SET_UDP_IP_PORT` | 位置在 `0x0004` 前后都出现过 |
| 由入站帧触发，不由玩家触发 | `0x00C5`（`0x00AD`）、`0x02A8` / `0x01BA`（名单） | 调用栈里有 NOTI 分发器帧 |
| 由玩家触发 | `0x02C1`、`0x0004`、`0x02B5`、`0x0005` | 调用栈里**没有**分发器帧，全在 UI 模块 |

## 两个仪器的盲区与对账

本页的 wire 结论来自两个仪器，它们的盲区不重叠：

| | 客户端抓包 `clienthack-frame-capture.log` | 运行时读取跟踪 `read-trace-20260817-entry-batch.log` |
|---|---|---|
| 给出 | 双向、有序、真实字节、出站调用栈、seq | 每一次入站读取的偏移/宽度/值/读取者 RVA |
| 不给出 | 字段边界；S→C 的 command 字节 | 出站帧；帧类型（借自解密 opcode） |
| 看不见 | 不走加密路径的发送；body 长度为 0 的帧；入站 NOTI `0x0001` | body 为 0 的帧（无读取即无记录）；会话最后一帧；入站 NOTI `0x0001` |

### 对账结果：本窗口内两者零冲突

会话 12 的入站帧：抓包 **74** 条，跟踪 **73** 行，
**按顺序逐帧一一对应**（type 与 body 长度全等），唯一差额是抓包的最后一条（`0x001B`，19 字节）
——跟踪把一帧写出的时机是"下一帧开始时"，所以最后一帧永远不落盘
（[`network_hook.c` 的 `flush_read_frame` / `set_remaining_trace`](../../ClientHack/plugins/0100_network_hook/network_hook.c)）。

这条 1:1 对应同时验证了跟踪"类型是借来的"这个假设在本会话上没有错帧。

### 判族：跟踪能给出抓包给不出的东西

跟踪日志不记 CMD/NOTI，但**首次读取的 `ret == 0x00D89B25` 的帧就是 CMD**——那是接收层读
结果信封 `ok` 的返回地址（[game-receive](game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)）。
本窗口内被判为 CMD 的入站帧是 `0x0001`、`0x0286`、`0x01BA`、`0x02C1`×2，
其余 8 条为 NOTI。三重交叉验证：

1. 抓包里 `0x02C1` 有明确的配对请求，`0x0001` 也有——CMD 无误；
2. 服务端编码器把这几条注册为 `AddGameCmdMessageEncoder`，其余为 Noti
   （[`ProtocolModule.cs`](../../Server/src/Dfo.Server.Protocol/ProtocolModule.cs)）；
3. 同一份跟踪在选角色批次里只把 `0x0004` 与 `0x0312` 判为 CMD，
   而 world-entry 记录的参照序列里恰好也只有这两条是 CMD
   （[world-entry.md 的顺序表](world-entry.md#顺序段内无序段间有序)）。

同一份跟踪里还同时出现了 `CMD 0x0286`（2 字节）与 `NOTI 0x0286`（22 字节）——
同号异义在一次会话内就能看到，[两张注册表](game-receive.md#两张注册表--cmd-与-noti)不是纸上结论。

### 抓不到的发送已经全部对上号

**本窗口内**每次会话固定缺两个 seq：`0x02A8` 出现的会话缺 4 和 7，其余缺 4 和 6；
建角色的会话每建一个再多缺一个。（进城之后另有缺口，属 world-entry 的范围。）
本窗口的缺口全部可归因：

| 缺的 seq | 帧 | 归因依据 |
|---|---|---|
| 4 | `CMD 0x0286`，body 0 | LOGIN handler `+0xC3A` 的构包点（本页）+ 服务端未识别帧日志（`catalog`） |
| 6 或 7 | `CMD 0x01BA`，body 0 | 名单 handler `0x00B569AD` 尾调用 `0x00CDDE80`（本页）+ 17/17 响应配对 |
| 建角色后每次多缺一个 | `CMD 0x01BA`，body 0 | 每份补发名单再触发一次；会话 2 缺 11/14/17（建了 3 个），会话 10 缺 11/14（建了 2 个） |

所以在这个窗口里，"客户端只发了这些帧"现在**可以**断言——不是因为抓包完整了，
而是因为每个缺口都被独立证据填上了。

## 证据

支持客户端 SHA-256：

```text
F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C
```

内存 dump：`DNFClient-debug/dumps/dnf-dump-20260817-100117-955-7824`（`image_base = 0x00400000`，
RVA/VA 换算见 [game-frame](game-frame.md#地址约定rva-还是-va先读这一节)）。

| 种类 | 位置 | 支撑 |
|---|---|---|
| 运行时读取跟踪 | [`data/traces/read-trace-20260817-entry-batch.log`](data/traces/read-trace-20260817-entry-batch.log) 第 1–13 帧 | 入站帧顺序、族归属、body 长度、字段边界 |
| 客户端抓包 | `D:\project\DNF\DNFClient-debug\clienthack-frame-capture.log`，12 次会话（行 1–1139），会话边界为 `send seq==0` | 双向顺序、seq、body 字节、出站调用栈 |
| 反汇编（静态） | 上列各地址段 | 四个接力点的因果链、加密分叉、`0x02A8` 的三个条件门 |
| 客户端普查 | [`data/protocol-identity.tsv`](data/protocol-identity.tsv) | handler 入口与构包点集合、族归属 |
| 服务端实现 | `Server/src/Dfo.Server.Protocol/`（各节链接） | 谁发什么、发在哪个 handler 里 |
| 上游转录（最低等级） | `.worktrees/legacy-reference/Server/DfoServer/Network/Handlers/LoginHandler.cs:83-91`、`CharacterSelectHandler.cs:432-569` | 批次成员与顺序的线索；`NOTI 0x0025` 差异 |

## 未解

| 问题 | 影响 | 最便宜的证据 |
|---|---|---|
| 频道端点的握手顺序、次数、是否重连 | 频道流程完全没有客户端侧证据 | 给抓包插件加频道端点的钩子（它的加密不是 `Cipher::*`） |
| `NOTI 0x0001` 的 body 布局与客户端消费方式 | 它是整条流程的起点，却是唯一两个仪器都看不见的帧 | 在 handler `0x00914A50` 入口下断；或给读取跟踪加一个"解密 opcode 为 -1 时按 `(0x00,0x0001)` 记账"的初值 |
| `0x018727E0` 那个固定密钥 XOR 到底做什么 | 决定初始通知能不能不打补丁地跑 | 反汇编 `0x018727E0`（在被 NOP 前它是唯一消费者） |
| 六条登录设置里哪几条承重 | 服务端能不能精简 | 逐条去掉重试，一次实机一条 |
| 六条是否必须排在名单帧之前 | 服务端批次的排序自由度 | 把名单批次插到六条之前跑一次 |
| `0x02C1` 那一字节的含义与往返次数的来源 | 服务端只能盲答全零 | 在构包点 `0x01C4104F` 下断看取值来源 |
| 名单 count 通过哪个字段影响 `0x02A8` 的三个门 | `0x02A8` 何时会发 | 在 `0x01C332F0` 入口读 `[ecx+0x70]`/`[ecx+0x9C]`/`[ecx+0xA0]`，空名单与非空各一次 |
| `0x01BA` 请求触发点在进城批次里还有第二个 | 12 次会话共 29 次 `0x01BA` 响应，其中 17 次来自名单，另 12 次（每会话一次）出现在进城批次里，来源未定 | 在 `0x00CDDE80` 下断，记调用栈 |
| 上游那条 `NOTI 0x0025`（账号经验进度）我们不发 | 未知；客户端在缺它的情况下正常 | 补发一次，看选人界面是否有可观察差别 |
| 登录被拒（`ok = 0`）的实际表现 | 服务端目前从不拒绝登录 | 让服务端回 `00 <reason>` 一次，看客户端提示 |

## 与协议层文档不一致之处

下面两条**不是本页要改的东西**（字段与触发的记载归各协议文档），但本页的证据与它们冲突，
搬家之前先记在这里：

1. **[`0x0286.md`](../game/client-to-server/0x0286.md) 的 `Trigger` 写"收到角色列表后立即发出"**——
   构包点在 LOGIN 响应 handler 内 `+0xC3A`，而 seq 4 在 `0x00C5`（seq 5，由名单之前 7 帧的
   `0x00AD` 触发）之前，所以它在名单**之前**发出。正确的说法是"LOGIN 响应 handler 的第四条"。
2. **[`0x02A8.md`](../game/client-to-server/0x02A8.md) 写"全区间内把 `+0x70` 置 1 的只有回包路径一处"**——
   置位指令 `0x01C3F52C` 确实只有一处，但它所在的函数 `0x01C3F4F0` 有**两个**调用者，
   其中一个是名单 handler 的 `0x00B5695C`（在原扫描区间 `0x01C30000`–`0x01C60000` 之外）。
   所以生命周期是"名单置位 → 发送清零 → 回包再置位"，而不是"回包首次置位"。
   该页的 A/B 结论（没有回包则建角色 UI 打不开）不受影响——**因为发送本身会清零**。

另有一条纯粹的收获，属于协议层，记在这里以免丢失：读取跟踪显示客户端把 `CMD 0x0001`
LOGIN 响应读到**偏移 45**（偏移 32 `u32`、36 `u32`、40 `u16`、42/43/44 各 `u8`），
而 [CMD-0x0001.md](../game/server-to-client/CMD-0x0001.md) 的静态字段表只到偏移 33，
把其后 20 字节记为"尚未解出"。实际未被读取的只有尾部 8 字节。

## 逆向工作日志

**2026-08-17**：本页是从既有的 12 次会话抓包 + 一份读取跟踪整理出来的，没有新做实机。
过程里有四条值得留下的东西，其中两条是弯路。

1. **弯路：先按"抓包里谁在谁前面"推因果，得出了两个错结论。**
   一个是"六条设置批次是 GET_USERINFO 的回答"（因为它们出现在 `0x0008` 之后），
   另一个是"`0x0286` 在名单之后发"（`0x0286.md` 至今这么写）。两者都错，错法相同：
   **客户端在响应到达之前就把下一批请求发完了**，wire 顺序里"A 在 B 之后"完全不含因果。
   *教训*：因果只能从两个地方来——**服务端代码**（谁在哪个 handler 里发）或**出站调用栈**
   （客户端在哪个 handler 里发）。抓包顺序只是这两者的投影。

2. **弯路：把"抓包和跟踪都没有这一帧"当成"这一帧不存在"。**
   NOTI `0x0001` 在两个日志里都没有，于是一开始认为服务端的初始连接通知没被客户端收到。
   是 LOGIN 的调用栈推翻了它——LOGIN 是从一个 NOTI handler 里发出来的，那条 NOTI 必然到过。
   顺着这条线才在接收函数里找到 `0x00D89950` 那道 `(command, type)` 分叉：
   **只有 `(0x00, 0x0001)` 不走 `Cipher::Decrypt`**，而两个仪器都挂在解密路径上
   （跟踪连帧的身份都借自解密 opcode，第一帧之前那个 opcode 还是 `-1`，记录直接被门掉）。
   *教训*：两个仪器同时沉默，先查它们的盲区**是不是同一个**。这里两者共用一个前提
   （帧走过 `Cipher::Decrypt`），所以它们不是互相独立的证据，加起来也不构成否证。

3. **调用栈里最有价值的不是第 1 帧，是分发器那两帧。**
   `00D8566E / 00D89C36` 是 CMD 分发，`00D8577A / 00D89CFC` 是 NOTI 分发，
   **两者一眼可分**。有这两帧就知道"这一帧是被入站帧触发的"，没有就是 UI 触发的。
   本页"等与不等"那张表一半的行来自这个判据，成本是零——抓包里本来就有。
   相比之下第 1 帧（构包器内的返回地址）经常需要再查一次普查表才有用。

4. **"只有一个调用者"是把链闭合的那把锁。**
   `0x00AB`/`0x01DD` 的调用栈中间缺帧（栈行走跳过了没有帧指针的函数），单看栈只能说
   "在某次 CMD 分发里"。全镜像扫 `E8 rel32` 发现 `0x01CD12C0` 只有 `0x008C7E7E` 一个调用者、
   `0x00C375B0` 只有 `0x01CD11CA` 一个调用者之后，链就没有歧义了。
   再加上"`0x008C7E20`–`0x008C8C00` 之间没有 `CC` 填充"这一条，四个构包点同属一个函数体也
   成了事实而不是推测。**扫调用者比补全调用栈便宜得多。**
