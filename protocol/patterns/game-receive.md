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
  ├─ call  0x00D89620                    ← body 解析入口（尚未展开）
  ├─ movzx ecx, byte [ebx]               ← 0x00D89ABC：读 header 第 0 字节 = command
  │    sub eax, 0 / je  0x00D89C62       ← command 0x00 → 分支 B
  │    dec eax     / je  0x00D89B16       ← command 0x01 → 分支 A
  │    否则记日志（行号 0x1FA）后不分发
  ├─ 分支 A: call 0x00D855E0             ← 查注册表 A；返回地址 0x00D89C36
  └─ 分支 B: call 0x00D856F0             ← 查注册表 B；调用点 0x00D89CF7
```

**command 决定查哪张表。** 两个分发器各只有一个调用点，都在本函数内、都以同一个 `this`
（`mov ecx, esi`）调用，互斥二选一——一帧只会进入其中一张表，既不并发分流也不切换。
详见[两张注册表](#两张注册表按-command-分流)。

已抓到的全部实帧都是 `command = 0x01`，即只走分支 A。

### 分发器 `RVA 0x00D855E0`（command 0x01）

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

**重要：handler 拿不到帧缓冲。** 它的四个参数是 `(type, arg2, arg3, context)`，
帧内容不在其中。所以这张注册表是**"某 type 到达了"的通知钩子**，不是 body 解析器；
body 解析在更早的 `0x00D89620` 里，那里才是读字段的地方（**尚未逆向，这是 S→C 的下一步**）。

## 两张注册表：按 command 分流

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

| | 表 A | 表 B |
|---|---|---|
| 单例 getter | `0x00D89F30` | `0x00D8A070` |
| orchestrator | `0x00D89D40` | `0x00D8A010` |
| 批量函数 | `0x008C5920`(41) `0x008D04C0`(75) `0x008F3C70`(216) | `0x0090BD20`(147) `0x009301C0`(210) `0x0093EAD0`(143) |
| 启动期注册点 | 332 | 500 |
| 不同 type | **331** | **499** |
| 分发器 | `0x00D855E0` | `0x00D856F0` |
| **command** | **`0x01`** | **`0x00`** |

**因此两张表是两个 command 命名空间，不是"多套 handler 集合按状态切换"。** 依据：

1. 接收函数在 `0x00D89ABC` 读 header 第 0 字节，`command==0x00` 跳 `0x00D89C62`（→ 表 B），
   `command==0x01` 跳 `0x00D89B16`（→ 表 A），二者互斥；两个分发器各只有一个调用点。
2. 两张表同时常驻，谁也不替换谁——各自的 init guard 是一次性的。
3. 两表都有的 222 个 type 里，**218 个的 handler 函数不同**，只有 4 个相同。说明 B 不是 A
   的遗留副本，而是另一套语义。

这与资料库的身份规则一致：**协议身份 = endpoint + direction + command + type**。已抓到的
实帧全部是 `command = 0x01`，所以**与当前工作相关的是表 A 的 331 个 type**；表 B 的 499 个
属于 command `0x00`，尚未观察到任何实帧。

> 从现有 TSV 复原分表：注册点 RVA `< 0x00900000` 属表 A，`>= 0x00900000` 属表 B
> （六个批量函数的地址区间天然分开）。TSV 本身尚未记录表归属，见下方"普查的已知缺口"。

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

- [`data/handler-census.py`](data/handler-census.py)
- [`data/server-to-client-handlers.tsv`](data/server-to-client-handlers.tsv)：
  `type` / handler RVA / handler VA / context / 注册点 RVA

| 指标 | 值 |
|---|---|
| `Register` 调用点 | 834 |
| 解析成功 | 832 |
| 不同 type（两表合计） | 608，范围 `0x0000`–`0x03F8` |
| ——其中表 A（command `0x01`） | **331** |
| ——其中表 B（command `0x00`） | **499** |
| 两表都出现的 type | 222（其中 218 个 handler 不同） |

### 普查的已知缺口：439 个运行时注册点

除六个批量函数外，还有两个 cdecl 转发器，把 registry 的获取**藏在自己内部**：

| 转发器 | 内部取表 | 调用点数 |
|---|---|---:|
| `0x00D855C0` | `call 0x00D89F30` → 表 A | 208 |
| `0x00D85680` | `call 0x00D8A070` → 表 B | 231 |

它们散布在整个 binary（`0x00026E7F`、`0x00092826`、`0x0014B6A1` …），**不在启动批次内**。
因为 Register 是覆盖语义，任何一次运行时重注册都会**改写**该 type 的 handler。这是
**rebind（重绑定）**，不是"换表"。

普查脚本只把这两个转发器体内的 `Register` 调用当成 2 行 `no-3-pushes` 未解析行
（`0x00D855D6` / `0x00D85696`），**439 个真实注册点从未进过 TSV**。
因此 331 / 499 都是**下限**。

这些重注册**是否发生在客户端状态迁移时**目前无证据——静态只能看到调用点位置。要确认必须
在 `0x00D855C0` / `0x00D85680` 下断点跑一遍登录 → 选角 → 进图。

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
| 规模 | 1082 点 / 604 type | 832 点 / 608 type（表 A 331 + 表 B 499） |
| 一对多 | 一个 type 多个构包点（平均 1.79） | 同表内 type → handler 唯一（覆盖语义）；222 个 type 在两张表各有一份 |
| 盲区 | 被混淆的第二代码区扫不到 | handler 拿不到 body；439 个运行时注册点未收录 |

两者都用同一套「前向对齐投票」取立即数，理由见 game-frame。
