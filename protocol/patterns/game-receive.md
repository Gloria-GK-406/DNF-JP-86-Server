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
接收函数 (…0x00D899xx–0x00D89Dxx)
  ├─ sub  ecx, 0x0F                      ← S→C header 15 字节（与既有结论一致）
  ├─ call 0x00D89620                     ← body 解析入口（尚未展开）
  ├─ switch ([ebx])                      ← 按 command 字节分支，cmd0 → 0x00D89C62
  └─ call 0x00D855E0                     ← 按 type 分发；返回地址 0x00D89C36
```

### 分发器 `RVA 0x00D855E0`

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

## 接收 handler 注册表普查

注册表本身是运行时哈希表，而 dump 里的运行时数据区全是零，读不到。但**注册动作是静态的**：
三个批量函数（`RVA 0x008F3C70` / `0x008C5920` / `0x008D04C0`，由 `0x00D89D40` 依次调用）
按固定形状逐条注册：

```asm
push  <context>
push  <handler VA>
push  <type>
mov   ecx, <registry>
call  0x00D89FC0            ; Register(type, handler, context)
```

`Register` 的实现确认了参数语义：`[ebp+8]` 作哈希表键，`[ebp+0xC]` 存进 `record[0]`，
`[ebp+0x10]` 存进 `record[4]`——正是分发器读的两个槽。

工具与结果已入库：

- [`data/handler-census.py`](data/handler-census.py)
- [`data/server-to-client-handlers.tsv`](data/server-to-client-handlers.tsv)：
  `type` / handler RVA / handler VA / context / 注册点 RVA

| 指标 | 值 |
|---|---|
| `Register` 调用点 | 834 |
| 解析成功 | 832 |
| 不同 type | **608**，范围 `0x0000`–`0x03F8` |
| 注册多次的 type | 231 个（不同批次注册不同 handler） |

**一个 type 注册多次**说明存在多套 handler 集合（按连接种类或客户端状态切换）。
`0x0001` 就有两个：`RVA 0x008C7E20`（批次 `0x008D04C0`）与 `RVA 0x00914A50`
（批次 `0x009301CC` 一带）。**因此不能假定"type → 唯一 handler"**；要先确定当前阶段用的
是哪一批。

### 正确性校验（必须做）

普查结果用抓包栈交叉验证：C→S `0x0008` 的发送点在 `RVA 0x008C86CF`，它落在
**为 type `0x0001` 注册的 handler**（入口 `RVA 0x008C7E20`，偏移 `+0x8AF`）内部——
即客户端在处理 LOGIN 响应时发出 GET_USERINFO，与抓包顺序完全一致。

同一校验也说明了栈深度的读法：`0x00AB` / `0x01DD` 的发送点**不在** handler 入口的合理偏移内
（差 `+0xAA59BA` / `+0x2F8ED3`），因为它们是 handler 再往下调用的函数发出的——
`0x01DD` 少一层、`0x00AB` 多两层，与各自记录里的栈层数一致。

## 与 C→S 普查的对照

| | C→S | S→C |
|---|---|---|
| 锚点 | `MakeHeader` `0x1C90460` 的直接调用点 | `Register` `0x00D89FC0` 的直接调用点 |
| 得到 | type + 构包点 | type + handler 入口 |
| 规模 | 1082 点 / 604 type | 832 点 / 608 type |
| 一对多 | 一个 type 多个构包点（平均 1.79） | 一个 type 多次注册（231 个 type） |
| 盲区 | 被混淆的第二代码区扫不到 | handler 拿不到 body，解析在别处 |

两者都用同一套「前向对齐投票」取立即数，理由见 game-frame。
