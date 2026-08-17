# Game 接收路径（S→C）

C→S 的做法（`MakeHeader` 普查 + 写入原语）见 [game-frame.md](game-frame.md)。
S→C 是**另一套机制**，本页记录它。地址约定（RVA / VA）见
[game-frame 的地址约定小节](game-frame.md#地址约定rva-还是-va先读这一节)——本页所有
`RVA` 标注都按那条规则换算过。

- 支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

## 为什么调用栈在 S→C 上不管用

抓包插件在 `Cipher::Decrypt` 上取栈，而**分发发生在 Decrypt 返回之后**，所以每一条入站帧
记录到的栈完全相同，不含任何 per-type 信息：

```text
所有 recv 帧: 00D89A3B  01C90D3B  00C601BA  ...    ← 逐帧一致
```

`RVA 0x00D89A3B` 是解密调用的返回地址，位于接收函数内部。因此 S→C 必须换一条路：
找分发表。

## 接收链

```text
接收函数 RVA 0x00D8977C
  ├─ sub   ecx, 0x0F                     ← S→C header 15 字节（与既有结论一致）
  ├─ call  0x00D89620                    ← 不是 body 解析：只特判 type 0x02/0x09/0x57
  ├─ movzx ecx, byte [ebx]               ← 0x00D89ABC：读 header 第 0 字节 = command
  │    sub eax, 0 / je  0x00D89C62       ← command 0x00 → 分支 B
  │    dec eax     / je  0x00D89B16       ← command 0x01 → 分支 A
  │    否则记日志（行号 0x1FA）后不分发
  ├─ 分支 A: call 0x00D855E0             ← 查注册表 A；返回地址 0x00D89C36
  └─ 分支 B: call 0x00D856F0             ← 查注册表 B；调用点 0x00D89CF7
```

**command 决定查哪张表。** 两个分发器各只有一个调用点，都在本函数内、都以同一个 `this`
（`mov ecx, esi`）调用，互斥二选一——一帧只会进入其中一张表，既不并发分流也不切换。
两张表分别对应 **CMD** 与 **NOTI** 两个协议族，详见[CMD 与 NOTI](#两张注册表--cmd-与-noti)。

### 分发器 `RVA 0x00D855E0`（CMD，command 0x01）

```asm
mov   eax, [ebp+8]          ; arg1 = 帧缓冲
movzx esi, word [eax+1]     ; type = 帧偏移 1 处的 u16
call  GetWriter             ; 0x1C8F5B0
call  0x01C8F570            ; 某个前置判定
test  al, al
je    dispatch
cmp   esi, 3                ; type 3(EXIT) 无条件放行
je    dispatch
  ... 记录一条日志（行号 0x4DB）后直接返回，不分发 ...
dispatch:
push  esi
call  0x00D89F30            ; 取注册表单例（cdecl，压入的 type 留给下一次调用）
mov   ecx, eax
call  0x00D8A030            ; Find(type) → 记录指针，找不到则 0
test  eax, eax
je    done
mov   ecx, [eax+4]          ; record[4] = context
mov   edx, [eax]            ; record[0] = handler
push  ecx / push arg3 / push arg2 / push esi
call  edx                   ; ← 返回地址 0x00D8566E
```

**handler 拿不到帧缓冲**——它的四个参数是 `(type, arg2, arg3, context)`，帧内容不在其中。
但这不代表它读不到 body：handler 从**一个全局入站流**里自己拉字段，见下节。
`arg2`/`arg3` 也不是无名参数，它们来自 body 开头的两字节信封，见下节。

> 早期记录曾把 `0x00D89620` 写成"body 解析入口"，**是错的**。该函数只按 type 做特判
> （`cmp eax,0x57 / jg`，另特判 `0x02`、`0x09`、`0x57`），其余直接 `mov al,1; ret`，
> 不碰 body。两个分发器里都调的 `0x0122B910` 也曾被当成日志函数，它是个**纯 `ret` 空桩**。

### CMD body 的两字节结果信封（**每一条 CMD 都有**）

分发之前，接收函数在 `command == 0x01` 分支上先从入站流里读掉一到两个字节
（`RVA 0x00D89B16` 起，**不按 type 区分**）：

```asm
0x00D89B16  lea ecx,[ebp-0x64] / push 1 / push ecx
0x00D89B20  call ReadU8               ; body[0] → ok，恒读
0x00D89B3B  cmp byte [ebp-0x64], 0
0x00D89B4A  jne 0x00D89B73            ; ok != 0 → 不再读第二字节
0x00D89B52  call ReadU8               ; body[1] → reason，仅 ok == 0 时读
...
0x00D89C22  mov eax,[ebp-0x68]        ; reason
0x00D89C25  mov ecx,[ebp-0x64]        ; ok
0x00D89C28  push eax / push ecx / push ebx
0x00D89C31  call 0x00D855E0           ; 分发器再把它们透传给 handler
```

所以 handler 里的 `[ebp+0x0C]` 是 **ok**、`[ebp+0x10]` 是 **reason**，而它随后读到的流
**已经越过信封**。也就是说：

```text
CMD body = u8 ok                    ; 非 0 = 成功
         + u8 reason                ; 仅当 ok == 0 才存在
         + <该 type 自己的载荷>      ; 仅成功路径读得到
```

**结果是 CMD 族的属性，不是某一条消息的字段。** NOTI 没有信封——它的分支
（`RVA 0x00D89C62`）分发前一个字节都不读，整个 body 归 handler。

两条最容易踩的推论：

| 错误写法 | 客户端实际读到 | 症状 |
|---|---|---|
| 拒绝时只发 `00`，不带 reason | 第二个 `ReadU8` 照读不误，**吃掉下一帧的首字节** | 此后每一帧错位，且不报错 |
| 把状态码放首字节（如 `15`、`04`） | 非 0 = **成功**，然后去解析根本没发的载荷 | 客户端要么静默返回，要么按不存在的数据动作 |

reason 也不是自由取值：每条 type 的 handler 拿它跟一张固定表逐个 `cmp`，表外的值
**不匹配任何分支、静默返回**——从玩家角度和丢包完全一样。

这条信封在 2026-08-16 之前一直被误读成"每条消息各自的状态字节"，
[角色生命周期协议族](character-lifecycle.md#两字节结果信封) 记录了它造成的四个缺陷。

### 入站流与读取原语（S→C 的取字段方式）

全局结构位于 `VA 0x031C7E00`，与 C→S 的写入原语完全对称：

| 偏移 | 含义 |
|---|---|
| `+0x00` | 读游标 |
| `+0x04` | 剩余字节（减到负数触发回填） |
| `+0x08` | refill 回调（唯一装填点 `0x01CFFD59`） |
| `+0x0C` | 回调上下文 |
| `+0x20` | 回填抑制标志 |

| RVA | 语义 | 调用形态 |
|---|---|---|
| `0x01D00420` | `ReadU8(dest)` | `push 1` → `push &dest` |
| `0x01D00490` | `ReadU16(dest)` | `push &dest` |
| `0x01D00500` | `ReadU32(dest)` | `push &dest` |
| `0x01D00570` | `ReadBytes(dest, len)` | `push <len>` → `push &dest` |

`ReadU8` 一家共 1667 个调用点。**因此 S→C 的 body 布局可以像 C→S 一样从 handler 指令机械
读出，不必先抓包**——这是此前记为"S→C 的下一步"的那一环。

#### 多发无害，少发才致命

此前这里写的是"对游标的全部 5 处写入都是原语自身的步进，没有'每帧把游标指向 body'的地方"。
那个结论只查了 `+0x00`（游标）的写入点，漏掉了 `+0x04`（剩余字节）——它的赋值不是内联的，
而是一个独立的小 setter：

```asm
0x01CFF6A0  push ebp / mov ebp,esp
0x01CFF6A3  mov  eax, [ebp+8]
0x01CFF6A6  mov  [0x031C7E04], eax      ; SetRemaining(n)
0x01CFF6AB  pop  ebp / ret
```

它在 `.text` 里只有 3 个调用点，其中**两个在接收函数内**（`0x00D89905`、`0x00D89AA2`）。
第一个把参数算得很清楚：

```asm
0x00D898E9  mov  eax, [ebx+3]      ; 帧头 +3 = totalLength
0x00D898EF  sub  eax, 0x0F         ; 减去 15 字节服务端帧头 → body 长度
0x00D89904  push eax
0x00D89905  call 0x01CFF6A0        ; SetRemaining(bodyLength)
```

所以**每帧在分发前把剩余字节重设为该帧的 body 长度**。由此得到一条不对称的规则：

| handler 读的量 | 后果 |
|---|---|
| **少于** body | 剩下的字节在下一帧重设时被丢弃——**无害** |
| **多于** body | 剩余减到负数触发回填，从**下一帧**里取字节——此后错位 |

这正是 `0x0003` 裸 `00` 那个已证故障的机制，也解释了为什么它只朝一个方向发作。

实机旁证（2026-08-16）：服务端给 NOTI `0x00AD` 发的是 `u32(134) + 134B + 8 个零字节`，
而客户端回存 `0x00C5` 时只回 `u32(134) + 134B`——它不认那 8 个尾字节。可紧随其后的
`0x01C7` 与 192 字节角色名单全部解析正常。

**对服务端实现的直接指导：body 宁可长、不可短。** 当某条协议的 handler 读取量只能走出一个
下界（走到打包区就断了，这是 S→C 的常态），按下界发是安全的；按猜测的"精确长度"发，
一旦猜小就会连累其后每一帧。

`+0x08` 的回调是**装填**入口（`0x01CFFD59` 是它的注册点，不是回填本身），
所以这仍然是**带回填的流**，不是一次性缓冲区——只是每帧都重新划定了它的可读边界。

抽样统计：表 B（NOTI）28 个 handler 中 21 个会读入站流，表 A（CMD）24 个中 18 个——
**读字段是常态（约 75%）**，完全不读的 handler 属于少数，通常是纯触发型。

### 四个原语是唯一出口

这件事有一个直接推论：**打包区挡不住对字段的观察**。被混淆的代码也得调这四个函数才能取到
字节，所以在原语上取样，等于在所有 handler 的必经之路上取样——不管那个 handler 在不在
可跟进的区域里。

运行时读取跟踪就建立在这上面，它是本仓最高等级的证据种类；效力边界与用法见
[evidence](../evidence.md#效力顺序)。下面两条是它依赖的**帧层事实**，放在这里而不是那边。

#### 游标是裸指针，帧原点要借 `SetRemaining`

`+0x00` 是指针不是偏移，所以"body 内的第几字节"必须有原点。`SetRemaining` 就在接收函数里
紧贴着 header command 的读取和分发：

```asm
0x00D89AA2  call SetRemaining
0x00D89ABC  movzx ecx, byte [ebx]   ; header[0] = command
0x00D89AC4  je   …                  ; 0x00 → NOTI
0x00D89ACB  je   …                  ; 0x01 → CMD
```

那一刻的游标就是 body 原点。**这个模型自带验证**：模型对的话，CMD 帧的第一条记录必然是
`off:0 w:1`，且读取者是接收层那次信封读取。实测 `0x0004` 首条为
`off 0 w1 v1 ret=0x00D89B25`——正是[结果信封](#cmd-body-的两字节结果信封每一条-cmd-都有)那次
`ReadU8` 的返回地址。

`SetRemaining` 在 `.text` 里有两个调用点都在接收函数内，所以按它切帧要写成"有读取记录才切"。

#### 首读能判族

trace 记不到 selector（原语只看得见流），而同一个 type 在两族里是两条无关协议。但数据自己
能判：**CMD body 以结果信封开头，由接收层读掉**，所以首次读取来自 `0x00D89B25` 的帧是 CMD，
其余是 NOTI。2026-08-17 与服务端逐帧发送日志对账：这样判出的 8 帧恰好是它以 `selector=0x01`
发出的那 6 个 type，一条不多一条不少。

## 两张注册表 = CMD 与 NOTI

客户端有**两张并存的注册表**，各自是进程级懒汉单例，在同一处一次性建起来
（`RVA 0x00D8878F`）：

```asm
call 0xd89f30      ; GetRegistryA 单例（init guard [0x30913F0]，对象 0x1C 字节）
mov  ecx, eax
call 0xd89d40      ; orchestrator A → 3 个批量函数 → 332 条注册
call 0xd8a070      ; GetRegistryB 单例（init guard [0x30913FC]，对象 0x1C 字节）
mov  ecx, eax
call 0xd8a010      ; orchestrator B → 另 3 个批量函数 → 500 条注册
```

两个 orchestrator 都是 `mov esi, ecx` 后把**同一个 `this`** 反复传给自己名下三个批次，
所以每个 orchestrator 名下的三批注册进同一张表。

| | 表 A = **CMD** | 表 B = **NOTI** |
|---|---|---|
| command | **`0x01`** | **`0x00`** |
| 方向 | 双向（C→S 请求 / S→C 响应） | **仅 S→C**（服务端主动推送） |
| 客户端是否发送 | 是，且是唯一发送通道 | 从不 |
| 单例 getter | `0x00D89F30` | `0x00D8A070` |
| orchestrator | `0x00D89D40` | `0x00D8A010` |
| 批量函数 | `0x008C5920`(41) `0x008D04C0`(75) `0x008F3C70`(216) | `0x0090BD20`(147) `0x009301C0`(210) `0x0093EAD0`(143) |
| 分发器 | `0x00D855E0` | `0x00D856F0` |
| 已普查 type | 静态 331 + 动态 207 = **538**，max `0x0435` | 静态 499 + 动态 229 = **728**，max `0x0405` |
| type 空间容量 | **1086** | **1036** |

两个 orchestrator 都是 `mov esi, ecx` 后把**同一个 `this`** 反复传给自己名下三个批次，
所以每个 orchestrator 名下的三批注册进同一张表。

### 为什么是 CMD/NOTI 而不是"两个匿名 command 值"

1. **旧服自己的命名。** [SelectCharacterMigrationMatrix](../../Docs/Refactor/SelectCharacterMigrationMatrix.md)
   共 78 行带 `<族> <cmd>/<type>` 标注：`CMD` 全部是 `01/xxxx`（4 行），`NOTI` 全部是
   `00/xxxx`（74 行），**零例外**。
2. **客户端发送侧把 command 写死成 1。** `MakeHeader`（`0x01C90460`，所有出站帧的唯一必经点）
   在栈上拼 13 字节头后交给 `ISocket::Write`：

   ```asm
   mov  word  ptr [ebp-0x13], di   ; +1  type
   mov  byte  ptr [ebp-0x14], 1    ; +0  command ← 立即数，写死
   mov  dword ptr [ebp-0x11], 0    ; +3  totalLength 占位
   push 0xD / lea edx,[ebp-0x14] / call 0x048FF9AB
   ```

   所以**客户端只发 CMD，永远不发 NOTI**；NOTI 是单向的服务端推送通道。
3. **type 空间容量把两张表钉死。** 服务端在初始连接通知里告诉客户端
   `CommandPacketCount = 1086`、`NotificationPacketCount = 1036`
   （见 `Dfo.Server.Protocol/Game/InitialConnection/GameInitialConnectionPayloadCodec.cs`）。
   表 A 的最大 type 是 `0x0435` = **1077 > 1036**，装不进 NOTI 空间，只能是 CMD；
   表 B 最大 `0x0405` = 1029 < 1036，与 NOTI 相符。C→S 最大 type `0x043D` = 1085，
   **1085 + 1 = 1086**，正好用满 CMD 空间——客户端发送的 type 同样只能属于 CMD。
4. **服务端同一个 type 走两条 command。** `GameLoginResponseEncoder.EncodeSuccess` 发
   `(0x01, 0x0001)`，`GameInitialConnectionPayloadCodec.Encode` 发 `(0x00, 0x0001)`。
   同一 type、两条不同消息、两张不同的客户端表。
5. **两表都有的 389 个 type 中绝大多数 handler 不同**（静态部分 222 个里 218 个不同），
   B 不是 A 的遗留副本。

因此资料库的身份规则有了机制解释：**协议身份 = endpoint + direction + command + type**，
`type` 单独不构成身份，因为客户端真的用两张互不相干的哈希表。

**对服务端的直接约束**：command 选错，客户端会分发到另一张表，多半查不到 handler 就
**静默丢弃**（`Find` 返回 0 直接 return，不报错、不记日志）。这类 bug 没有任何症状。

> 从现有 TSV 复原静态分表：注册点 RVA `< 0x00900000` 属表 A，`>= 0x00900000` 属表 B
> （六个批量函数的地址区间天然分开）。TSV 尚未记录表归属，也尚未收录动态注册点。

### 抓包对 S→C 的 command 字节零信息

`clienthack-frame-capture.log` 里 `send` 恒为 `"hdr":1`、`recv` 恒为 `"hdr":0`，
**这不是 command 字节**。插件源码里两者都是字面量：`hdr` 是**证据强度标志**——出站能读到
body 指针前的 13 字节头，入站的解密输入前面没有头，只能取 `Cipher` 的 type 参数，
所以标 `hdr:0`。较新的抓包版本另有 `"cmd"` 字段才是 `frame[0]`，但**当前部署的构建还没有**。

结论：S→C 的 command 只能从服务端编码器与客户端分发逻辑推断，不能引用抓包。

## 接收 handler 注册表普查

注册表本身是运行时哈希表，而 dump 里的运行时数据区全是零，读不到。但**注册动作是静态的**：
六个批量函数按固定形状逐条注册：

```asm
push  <context>
push  <handler VA>
push  <type>
mov   ecx, esi              ; esi = 批量函数自己的 this = 所属注册表
call  0x00D89FC0            ; Register(type, handler, context)
```

注意 `ecx` **不是立即数**——registry 由调用方经 `this` 传入，所以静态只看注册点无法判断
写进了哪张表，必须回溯到批量函数的调用者。

`Register` 的实现确认了参数语义：`[ebp+8]` 作哈希表键，`[ebp+0xC]` 存进 `record[0]`，
`[ebp+0x10]` 存进 `record[4]`——正是分发器读的两个槽。

### Register 是覆盖语义

`0x00D89FC0` 先 `malloc(8)` 建记录（`[0]`=handler，`[4]`=context），再
`call 0x00D89DE0` 做 find-or-insert（key 已存在时走 `0x00D89EA0` 直接返回既有节点），
最后 `mov dword ptr [eax+4], esi` **直接改写节点的值槽**。

所以**同一张表内每个 type 任意时刻只有一个 handler，后注册的顶掉先注册的**——不是
multimap、不是链表、不是回调栈。同表内真正被重复注册的 type 只有 2 个（A、B 各 1）。

工具与结果已入库：

- [`data/census-identity.py`](data/census-identity.py)：两个方向、两个协议族的统一身份普查
- [`data/protocol-identity.tsv`](data/protocol-identity.tsv)：
  `direction` / `family` / `type` / 注册点数 / handler RVA / context / 所在区 / 全部注册点 / 注册路径

### 注册有两条路径：启动期批量 + 运行时转发器

除六个批量函数外，还有两个 cdecl 转发器，把 registry 的获取**藏在自己内部**：

| 转发器 | 内部取表 | 调用点数 |
|---|---|---:|
| `0x00D855C0` | `call 0x00D89F30` → 表 A（CMD） | 208 |
| `0x00D85680` | `call 0x00D8A070` → 表 B（NOTI） | 231 |

它们散布在整个 binary（`0x00026E7F`、`0x00092826`、`0x0014B6A1` …），**不在启动批次内**。
调用形状与批量注册不同——`push` 与转发器调用之间夹着一个 0 参数 thunk `call 0x008BC420`：

```asm
push  <context>
push  <handler VA>
push  <type>
call  0x008BC420            ; 0 参数 thunk，不扰动参数块
mov   ecx, eax
call  0x00D855C0 / 0x00D85680
```

前向对齐投票的规则遇到 `call` 就清空候选，所以这 439 个点原本全部落空
（只在 TSV 里留下 `0x00D855D6` / `0x00D85696` 两行 `no-3-pushes`）。放行该 thunk 后
**439 个点全部解出**，没有一个未解析。

### 合并后的普查规模

| 指标 | 表 A（CMD） | 表 B（NOTI） |
|---|---:|---:|
| 启动期批量注册点 | 332 | 500 |
| 运行时转发器调用点 | 208 | 231 |
| 静态批量得到的 type | 331 | 499 |
| 运行时转发器得到的 type | 207 | 229 |
| **合计不同 type** | **538** | **728** |
| 最大 type | `0x0435` (1077) | `0x0405` (1029) |
| 空间容量 | 1086 | 1036 |

两条路径得到的 type 集合**零重叠**（动态 207/229 全是静态没有的），与"同表内一个 type 只有
一个 handler"的覆盖语义相符。两表都出现的 type 共 389 个，仅表 A 149 个，仅表 B 339 个；
S→C 合计 **1266** 个不同的 `(command, type)` 对。

表 A 与 C→S 构包集合的重叠达 **85.5%**（460/538），符合 CMD 是请求/响应共用一套编号；
表 B 只有 60.6%，符合 NOTI 是独立空间。

因为 Register 是覆盖语义，任何一次运行时重注册都会**改写**该 type 的 handler。这是
**rebind（重绑定）**，不是"换表"。这些重注册**是否发生在客户端状态迁移时**目前无证据——
静态只能看到调用点位置。要确认必须在两个转发器上下断点跑一遍登录 → 选角 → 进图。

> **2026-08-16 缺口已闭合。** 此前这里记着"动态部分尚未写回 TSV、脚本也没加表归属列与
> thunk 放行规则"。统一普查 [`data/census-identity.py`](data/census-identity.py) 两项都补上了：
> 每条记录带 `family` 列（由所属批量函数或转发器身份决定），439 个运行时转发器注册点
> 全部解出，**零未解析**。产出为 [`data/protocol-identity.tsv`](data/protocol-identity.tsv)。

### 分发前还有一道门

两个分发器在查表前都调 `0x01C8F570`（`GetWriter` 之后），返回非零即**不分发**：
表 A 分支记一条日志（行号 `0x4DB`）后返回，表 B 分支直接返回。表 A 上只有 `type 3`（EXIT）
无条件放行。这是目前唯一确认的"按运行时状态改变收包行为"的机制——但它决定的是**收不收**，
不是**怎么处理**。

### 正确性校验（必须做）

普查结果用抓包栈交叉验证：C→S `0x0008` 的发送点在 `RVA 0x008C86CF`，它落在
**表 A 为 type `0x0001` 注册的 handler**（入口 `RVA 0x008C7E20`，批次 `0x008D04C0`，
偏移 `+0x8AF`）内部——即客户端在处理 LOGIN 响应时发出 GET_USERINFO，与抓包顺序完全一致。
这同时印证了 command `0x01` 走表 A：`0x0001` 在表 B 里注册的是另一个 handler
（`RVA 0x00914A50`，批次 `0x009301C0`），与实测行为对不上。

同一校验也说明了栈深度的读法：`0x00AB` / `0x01DD` 的发送点**不在** handler 入口的合理偏移内
（差 `+0xAA59BA` / `+0x2F8ED3`），因为它们是 handler 再往下调用的函数发出的——
`0x01DD` 少一层、`0x00AB` 多两层，与各自记录里的栈层数一致。

## 与 C→S 普查的对照

| | C→S | S→C |
|---|---|---|
| 锚点 | `MakeHeader` `0x1C90460` 的直接调用点 | `Register` `0x00D89FC0` 的直接调用点 |
| 得到 | type + 构包点 | type + handler 入口 |
| 协议族 | 全部是 CMD（`MakeHeader` 写死 command 1） | CMD 表 A + NOTI 表 B |
| 规模 | 1082 点 / 604 type | 1271 点 / 1266 个 `(command,type)` 对（A 538 + B 728） |
| 一对多 | 一个 type 多个构包点（平均 1.79） | 同表内 type → handler 唯一（覆盖语义）；389 个 type 在两张表各有一份 |
| 取字段 | 写入原语 + `MakeHeader`/`Finalize` | 读取原语 + 全局入站流 `0x031C7E00` |
| 盲区 | 被混淆的第二代码区扫不到 | 约 25% 的 handler 不读 body，无法从指令推出布局 |

两者都用同一套「前向对齐投票」取立即数，理由见 game-frame。
