# Protocol 0x00AB 反汇编工作记录

> 状态：进行中。本文档记录客户端协议 `type=0x00AB`（171）的反汇编过程、已确认
> 事实、待办与方法论。所有结论均标注证据来源（动态抓包 / 内存读取 / 反汇编 / 推断）。

## 任务目标

- 搞清楚客户端发送的 `type=0x00AB` 帧记录了什么（body 字段语义）。
- 确定服务端需要返回什么（是否需要 ACK、ACK 格式）。
- 沉淀一套可复用的"未知协议反汇编"方法论。

## 方向与定位（已确认）

- **方向：client → server**。
  - 证据：NetworkHook 在 `Cipher::Encrypt`（发包路径）命中，`dir=send`。
  - 服务端确认从未发送过 type=0x00AB 的包。
- **cmd=0x01**：与 LOGIN(0x0001)、EXIT(0x0003)、GET_USERINFO(0x0008) 同命令分类，
  属于普通 Game 业务消息（13 字节客户端包头）。
- **时机**：登录频道后早期阶段，紧跟在 LOGIN 请求之后、GET_USERINFO 之前发送。
  一次性发送，非心跳（同一会话仅出现 1 次，重复登录字节一致 = 确定性上报）。
- **服务端现状**：type=0x00AB 不在 `NetworkingFrameProtocol.md`、`NetworkMessages.cs`、
  `GameProtocol.cs` 中，是服务端"未识别"的协议。

## 真实帧样本（动态抓包，证据强度：高）

抓包工具：扩展后的 `0100_NetworkHook.dll`（在 `Cipher::Encrypt` detour 内对
`packet_type==0xAB` 命中时 dump 整帧 + 调用栈）。来源文件
`clienthack-00ab-capture.log`。

```
完整帧（52 字节 = 13 头 + 39 body）:
01 AB 00 34 00 00 00 7A 8E 63 DE 01 00     ← header
80 00 04 00 0C 00 00 00 03 00 00 00        ← body[0:12]
01 86 80 00 00 A8 A7 00 00 02 0E 00 00     ← body[12:24]
00 00 00 00 00 03 00 00 00 00 07 00 00 00  ← body[24:39]
```

### Header 字段

| Offset | 值 | 含义 |
|---:|---:|---|
| 0 | `0x01` | cmd（Game 业务分类） |
| 1 | `0x00AB` | type（u16 LE，本协议 opcode） |
| 3 | `0x00000034`=52 | length（总帧长，已修正值） |
| 7 | `0xDE638E7A` | checksum（语义未确认） |
| 11 | `0x0001` | seq（首包） |

> 注：抓包瞬间读到的 length 字段是 MakePacket 留下的 stale 值（61），因为 dump
> 发生在 `Cipher::Encrypt` 写入正确 totalLength 之前。以 dump 出的实际字节数 52
> 为准。

### Body 字段（反汇编确认，证据强度：高）

通过定位 00AB 序列化函数（`0x1CD1120`）并反汇编其逐字段写入逻辑，body 的精确
结构已完全还原。**不是** u32 数组，而是 `u8 + u8 + u8 + 4 × (u8 + u32 + u32)`：

| Offset | 类型 | 值（本次样本） | 来源（反汇编） | 语义 |
|---:|---|---:|---|---|
| 0 | u8 | `0x80` | `arg1 - 0x80`（arg1=触发参数） | 触发标志/类别（见下） |
| 1 | u8 | `0x00` | `this->[0x34]`（发送前置 1） | 上报计数/标志（发送后置 1） |
| 2 | u8 | `0x04` | 常量 4 | **数组元素个数** |
| 3 | u8 | `0x00` | `this->[0x04]` | entry[0].tag |
| 4 | u32 | `0x0C`(12) | `this->[0x08]` | entry[0].v1 |
| 8 | u32 | `0x03`(3) | `this->[0x0C]` | entry[0].v2 |
| 12 | u8 | `0x01` | `this->[0x10]` | entry[1].tag |
| 13 | u32 | `0x00008086`(32902) | `this->[0x14]` | entry[1].v1 |
| 17 | u32 | `0x0000A7A8`(42920) | `this->[0x18]` | entry[1].v2 |
| 21 | u8 | `0x02` | `this->[0x1C]` | entry[2].tag |
| 22 | u32 | `0x0E`(14) | `this->[0x20]` | entry[2].v1 |
| 26 | u32 | `0x00`(0) | `this->[0x24]` | entry[2].v2 |
| 30 | u8 | `0x03` | `this->[0x28]` | entry[3].tag |
| 31 | u32 | `0x00`(0) | `this->[0x2C]` | entry[3].v1 |
| 35 | u32 | `0x07`(7) | `this->[0x30]` | entry[3].v2 |

合计 39 字节，**零剩余字节**，结构闭合。

**关键结构特征：**
- body[2]=常量 4，是**定长数组**（4 个槽位，不是可变长）。
- 4 个 entry 的 `tag` = **0,1,2,3 连续递增** → 是**枚举槽位编号**。
- 每个 entry = `(tag:u8, v1:u32, v2:u32)`，是**每槽位两个统计值**。
- `this` 对象（单例 `[0x3091FB8]`）布局：每槽 12 字节 × 4 = 0x30，加 `[0x34]` 标志。
- `[0x34]` 在发送后被置 1 → **"已上报"标志**，证实 00AB 是一次性上报。

**`arg1`（body[0]）的含义**：序列化器有 2 个调用路径——
- `0x1CD12C3 push 0` → body[0] = `0-0x80 = 0x80`（本次样本命中此路径）
- `0x1CD137C push 1` → body[0] = `0x81`（条件路径，需 `0x10F9DA0` + `0xB61F70` 为真）

所以 body[0] 是**触发场景标志**：0x80 = 主路径上报，0x81 = 条件触发上报。

### 业务语义推断（待人工确认）

00AB 是客户端在登录握手早期（LOGIN 之后、GET_USERINFO 之前）一次性上报的
**4 槽位状态报告**，每槽位两个 u32。`tag` 0..3 是槽位枚举。具体业务含义需结合
DNF 业务知识判断（候选：客户端配置/环境上报、功能解锁状态、统计计数器等）。

样本数据：
- entry[0] tag=0: v1=12, v2=3
- entry[1] tag=1: v1=32902(0x8086), v2=42920(0xA7A8)
- entry[2] tag=2: v1=14, v2=0
- entry[3] tag=3: v1=0, v2=7

> `0x8086` / `0xA7A8` 这种数值像是**版本号/位掩码/ID**而非计数。

## 调用栈（动态捕获，证据强度：高）

`RtlCaptureStackBackTrace` 在 00AB 命中时记录的 16 帧（已减去 DNF.exe base
0x400000 转 RVA）。过滤掉系统模块后，DNF.exe 内的关键帧：

| frame | RVA | 角色（反汇编确认） |
|---:|---:|---|
| 3 | `0x00D8566E` | `Cipher::Encrypt` 调度器（虚表调用 encrypt） |
| 4 | `0x00D89C0E` 区 | 取 type 字段 `[ebx+1]` → 查表 `[ecx*4+0x3174FF8]` → 准备 Encrypt 参数 |
| 5 | `0x01C90CF0` 区 | TCP_SEND（检查 staging `0xAF000` 上限，取 staged 包） |
| 6 | `0x00C601BA` 区 | packet pop / 调度 |

### 关键反汇编片段（frame4，`0x00D89C0E` 起）

```
0x00D89C0E  movzx ecx, word ptr [ebx + 1]      ; ecx = type 字段（=0x00AB）
0x00D89C12  mov  edx, [ecx*4 + 0x3174FF8]      ; edx = cipher_table[type]（slot 0xAB=0，默认加密）
0x00D89C19  push edx
0x00D89C1A  lea  ecx, [ebp - 0x78]             ; 栈缓冲
0x00D89C1D  call 0x20E1570
0x00D89C22  mov  eax, [ebp - 0x68]             ; size
0x00D89C25  mov  ecx, [ebp - 0x64]             ; ptr
0x00D89C31  call 0x11855E0                     ; → frame3 (Encrypt 调度)
```

**结论**：body 在到达 `Cipher::Encrypt` 之前已组装好，存于 staging buffer。
body 的真正 builder 在 `ISocket::Write` 的调用者（业务层），尚未定位到具体函数。

### 数据表 `0x3174FF8`

按 type 索引的函数指针表，slot[0xAB]=0（NULL），表示 0xAB 无特殊加密路径，
走默认。**不是**业务 handler 分发表。

## 内存取证

在运行内存中搜索 body 前缀 `80 00 04 00 0C 00 00 00 03 00 00 00`，**唯一命中**
`0x2424F25B`。该地址所在缓冲是一个**双向会话日志**（混杂 client→server 与
server→client 帧，且包含启动参数中的 auth hash 字符串）。00AB 帧在其中的内存序：

| 相对偏移 | type | 方向（推断） | 身份 |
|---:|---:|---|---|
| -86 | `0x0001` | C→S | LOGIN 请求 |
| **0** | **`0x00AB`** | **C→S** | **本协议** |
| +52 | `0x01DD` | ? | 未知 |
| +81 | `0x0008` | C→S | GET_USERINFO 请求 |
| +97 | `0x0286` | S→C | 荣誉/成长胶囊通知（文档） |
| +110 | `0x02A8` | S→C | 未知通知 |
| +125 | `0x01BA` | S→C | 未知通知 |
| +138 | `0x0002` | S→C | 角色列表通知（文档） |

**结论**：00AB 是登录握手序列的一环，位于 LOGIN 与 GET_USERINFO 之间。

## 完整发送链路（反汇编确认，证据强度：高）

通过扩展 `socket_write` detour 记录**每个** ISocket::Write 调用的前 8 字节 +
调用栈，定位到 00AB 的逐字段序列化路径。ISocket::Write 是**字段级单一收口**
（不是按整帧调用，而是每次写 1/2/4/8/13 字节）。

### 00AB 序列化函数 `0x1CD1120`

`__thiscall(ecx = this)`，`ret 4`（1 个 u8 栈参数 `arg1`）。完整反汇编：

```asm
0x1CD1120  push ebp; mov ebp,esp; push ebx; push esi; push edi
0x1CD1126  push 0xAB                       ; type = 0x00AB
0x1CD112D  call 0x208F5B0; ... call 0x2090460   ; MakeHeader(type) → Write 13字节头
0x1CD1139  mov al,[ebp+8]; sub al,0x80; movzx ecx,al; push ecx  ; Write u8(arg1-0x80)
0x1CD114E  movzx edx,[ebx+0x34]; push edx  ; Write u8(this->[0x34])
0x1CD115F  push 4                          ; Write u8(const 4)   ← 数组个数
0x1CD116D  lea esi,[ebx+8]; mov edi,4      ; esi=&entry[0], 循环4次
   loop:
0x1CD1175  movzx eax,[esi-4]; push eax     ; Write u8(entry.tag)
0x1CD1186  mov ecx,[esi];   push ecx       ; Write u32(entry.v1)
0x1CD1195  mov edx,[esi+4]; push edx       ; Write u32(entry.v2)
0x1CD11A5  add esi,0xC; dec edi; jne loop  ; esi += 12
0x1CD11AB  call 0x208FDD0                   ; finalize
0x1CD11B2  mov [ebx+0x34],1                 ; this->reported = 1
0x1CD11B6  mov al,1; ret 4
```

### 触发链（自底向上）

| 层 | RVA | 角色 |
|---|---|---|
| 序列化器 | `0x1CD1120` | 写 00AB 帧的全部字节，读 this 对象 |
| 入口包装 | `0x1CD12C0` | `mov ecx,[ecx+0x10]; push 0; call 序列化器`（本次命中） |
| 触发点 | `0x008C7E7E` | `mov ecx,[0x3091FB8]; call 入口` |
| 触发函数 | `0x008C7E20` | 带 SEH 的大业务函数（`sub esp,0x31C`） |

触发函数上下文：
```asm
0x8C7E4E  call 0xCBC420          ; 构造对象
0x8C7E66  call 0x2032F20         ; 条件检查（返回 bool）
0x8C7E6B  test al,al; jne ...    ; 条件跳过
0x8C7E73  call 0xF83CF0
0x8C7E78  mov ecx,[0x3091FB8]    ; 全局单例对象指针
0x8C7E7E  call → 00AB 序列化器
```

### 数据来源

00AB body 全部来自**全局单例对象** `[0x3091FB8]`，对象布局：

```
this + 0x04  tag0   + 0x08 v1_0  + 0x0C v2_0   ; entry[0]
this + 0x10  tag1   + 0x14 v1_1  + 0x18 v2_1   ; entry[1]
this + 0x1C  tag2   + 0x20 v1_2  + 0x24 v2_2   ; entry[2]
this + 0x28  tag3   + 0x2C v1_3  + 0x30 v2_3   ; entry[3]
this + 0x34  reported-flag（发送后置1）
```

上报后该单例指针被清空（`[0x3091FB8]` 读到 0），印证一次性上报。

## 编排函数与回包判定（反汇编确认，证据强度：高）

### 编排函数 = 触发函数 `0x008C7E20`

`sub esp,0x31C`（本地 796 字节，带 SEH），是登录早期的**大编排函数**。00AB 上报
只是其中一步。关键序列：

```asm
0x8C7E4E  call 0xCBC420           ; 构造对象
0x8C7E66  call 0x2032F20          ; 条件检查（返回 bool）
0x8C7E6B  test al,al; jne ...     ; 条件跳过
0x8C7E73  call 0xF83CF0
0x8C7E78  mov ecx,[0x3091FB8]     ; 单例
0x8C7E7E  call 0x1CD12C0          ; → 00AB 序列化器（发 00AB）
0x8C7E83  mov ecx,[0x3091FB8]
0x8C7E89  call 0x1CD11C0          ; 同一单例的状态查询（返回 bool，非发包）
0x8C7E8E  mov ecx,[0x319C26C]; call 0x20AA250   ; 另一全局对象的操作
0x8C7E99  ... 大量日志记录（push 字符串ID + push 行号 0x8EC/0x8F0/0x912 + call 0x20F9700）
0x8C8044  mov ecx,[0x3091F74]; call 0x1C81C60   ; 又一全局对象操作
... 后续连续操作多个全局对象 + 日志 ...
```

### 回包判定：00AB 是 fire-and-forget，**服务端无需 ACK**

证据（全部来自反汇编，非推测）：

1. **发完 00AB 后代码不等待**：`0x8C7E7E` 调用序列化器后，**紧接着** `0x8C7E83`
   继续对同一单例调 `0x1CD11C0`（纯状态查询，非发包），然后连续操作多个全局对象
   并写日志。**没有任何"读回包/等标志位/轮询/条件分支依赖 00AB 响应"的逻辑**。
2. **`0x1CD11C0` 是查询非发包**：它只 `mov ecx,[ecx+0xC]; test; setne al`——
   读单例字段返回 bool，不碰 socket，不写帧。
3. **00AB 本身有"已上报"标志**：序列化器末尾 `mov [ebx+0x34],1`。客户端用本地
   标志位防重复发送，**不依赖服务端确认**。
4. **符合一次性上报模式**：单例上报后被清空，没有重发/超时/补发逻辑。

> 结论：**服务端收到 00AB 后无需回任何 ACK**。客户端是单向状态上报。
> 服务端模拟实现只需解析并记录这 4 槽位数据即可。

### 协议簇：00AB 是一组"客户端状态广播"之一

`0x1CD1120`（00AB 序列化器）与 `0x1CD33A0`（**type=0xFA** 序列化器）位于同一函数簇，
都操作单例 `[0x3091FB8]`。它们是登录早期客户端状态广播的不同切片：

| type | 序列化器 | 上报内容 | 结构 |
|---|---|---|---|
| `0x00AB` | `0x1CD1120` | 单例本体的 4 槽位摘要 | 定长：u8+u8+u8 + 4×(u8+u32+u32) |
| `0x00FA` | `0x1CD33A0` | 单例 `[0x24]` 子对象的变长数组 | count + N×(u16+u8+u16+u16+u16) |

**单例 `[0x3091FB8]` 是一个大聚合对象**：
- 本体 `[0x04]`~`[0x34]`：4 槽位摘要（00AB 上报）
- `[0x24]` 指向子对象：含 `[0x898]`=count1、`[0x89C]`=count2、两个变长数组
  （0xFA 上报其中之一，每条目 10 字节）

### 业务语义（推断，基于结构特征）

00AB 是**客户端在登录早期向服务端一次性广播的"4 槽位状态摘要"**：
- 每槽位一个 `tag`(0..3 枚举) + 两个 u32 统计值。
- `tag` 连续递增 = 枚举槽位编号，不是动态数据。
- 配合 0xFA（详细变长数组）和 LOGIN 后的多个通知，构成完整的客户端初始化上报。

**具体槽位语义仍需 DNF 业务知识确认**（候选：功能解锁标志、客户端能力位、配置版本、
统计计数器）。但服务端实现层面，解析结构已完全确定，可先按结构收存，语义后续标注。

## 项目前提（重要）

本项目**只有客户端，没有服务端**——服务端是从零模拟的目标，不是参考来源。因此：

- **没有多样本对比**：00AB 只有一份抓包样本（同一账号同一会话）。
- **没有服务端语义先验**：枚举名、handler、文档、"未知 opcode"日志统统不存在。
- 字段语义**只能靠硬反汇编客户端的 body builder 代码**逐字段确定。

> 因此本协议文档不存在"待服务端提供"的依赖项。一切结论必须来自客户端二进制。

## 反汇编策略：定位发送入队函数（单一收口）

### 假设

客户端发包被**队列化**：业务代码把 message（type+body）丢进发送队列，
由专门的工作线程（调用栈 frame7-10）取出、加密、flush。如果是这样：

- **入队函数是单一收口**：所有 message 共用同一个 `EnqueuePacket(type, body, len)`。
- 找到它 → 一次性枚举所有 message 发送点；其调用者里"push 0xAB"的就是 00AB builder。
- 入队函数签名揭示 body 内存模型（裸 buffer / packet 对象 / chain）。

### 已有线索（来自调用栈 + 内存取证）

- frame5 `0x01C90CF0` 区：`cmp esi, 0xAF000`（staging 上限）+ `call 0x208F850` /
  `call 0xCBC420` + `call 0x11897C0`——其一可能是入队/序列化。
- frame6 `0x00C601BA` 区：`push` 序列 + 虚函数调用 `call edx`——可能是 packet pop。
- body 唯一内存命中 `0x2424F25B` 所在缓冲 = 入队函数的写入目标。

### 实际走通的路径（取代上面的验证步骤）

见上文"完整发送链路"章节。关键转折是：**ISocket::Write 是字段级单一收口**——
不是按整帧调用，而是每写一个 u8/u16/u32 就调一次 Write。因此给 Write detour 加
"记录前 8 字节 + 调用栈"后，每个 message 的每个字段写入都能定位到具体指令地址，
builder 序列化函数自然浮现。

## 方法论：未知协议反汇编的高效流程

本次实战沉淀的、可复用于其他未知 opcode 的反汇编流程：

### 核心原则

**先动态拿样本定锚，再带具体字节反汇编；先找单一收口，再发散到各 builder。**
不要一开始就静态扫常量 / 硬啃汇编——加壳二进制静态读不到代码，且常量命中噪音大。

### 五步流程

1. **找现成 hook 点拿真实帧**（5 分钟）。
   不要急着上调试器。复用已有的 cipher/socket hook（本项目的 NetworkHook），在
   detour 里加"opcode 命中时 dump 整帧 + 调用栈"。一次重跑 = 真实字节样本 + 发包
   路径调用链。这是所有后续工作的锚点。

2. **确认发送架构的"收口"**（关键洞察）。
   判断发包是"业务代码直接 send"还是"经队列"。如果是队列化，找单一收口函数
   （本项目 = ISocket::Write）。**给收口函数加全量 trace（前 N 字节 + 调用栈），
   一次性拿到所有 message 的 builder 映射**——这是性价比最高的一步，后续每个
   协议都能复用这张图。

3. **从 trace 定位目标 opcode 的序列化函数**。
   在 trace 里找带头部 `cmd/type/len` 的那条 Write，其调用栈 frame2~3 就是序列化
   函数。本项目 00AB 序列化器 = `0x1CD1120`，由 trace 直接命中，无需 x64dbg。

4. **反汇编序列化函数 → 逐字段还原 body 结构**。
   读"它调用哪些 Write 原语（写 u8/u16/u32）+ 每次的值来自哪个对象成员/常量"，
   直接还原 body 的 C 结构。本项目据此把 39 字节精确解构为
   `u8+u8+u8+4×(u8+u32+u32)`，零剩余字节，并还原了 this 对象布局。

5. **沿调用者上溯到业务触发点**（理解语义）。
   用 `E8 rel32` 扫描找序列化函数的直接调用者（通常 1~2 个），再上溯一层到业务
   函数。本项目：序列化器 → `0x1CD12C0`(push 0) → `0x8C7E7E` → 全局单例
   `[0x3091FB8]`。数据对象的填充逻辑 = 业务语义来源。

### 关键技巧与坑

- **加壳二进制必须从运行进程内存读**（ReadProcessMemory），磁盘 PE 的 `.text`
  是 0 字节。RVA 转换用 PE 节区映射，但代码段直接 `BASE + RVA` 从进程内存读即可。
- **detour 里记调用栈**用 `RtlCaptureStackBackTrace`，frame 转成 RVA（减 dnf_base），
  可复现。设 `SKIP=2` 跳过插件自身帧。
- **detour 的越界陷阱**：判定期目标时只看函数参数（如 `packet_type`），**绝不要**
  去读 `input ± N` 的邻近内存——收发方向的包头长度不同（13 vs 15 字节），
  误读会每包触发、破坏收包路径。本次踩过此坑（见 git 历史）。
- **静态扫 opcode 常量（`push 0xAB`）噪音极大**（本项目 117 个命中，多数假阳性）。
   只在拿到 trace 锚点后用它做交叉验证，不要用它做主路径。
- **trace 的"字段级 Write"特性**比"整帧 Write"更有价值：每个字段的写入指令地址
   直接对应序列化器里的一条 `push/mov`，反汇编时一目了然。
- **内存搜 body 字节**可定位数据缓冲，但要区分"发送 staging"和"会话日志缓冲"
  （后者混杂双向帧，易误导方向判断）。
- **唯一单例指针 `[0x3091FB8]` 发送后被清空** → 一次性上报的强证据；想读对象值要
  在发送前下断，或直接从已抓 body 反推。

### 可复用产物

- `socket_write` 的 trace 逻辑（`trace_socket_write`）是**通用 message 发现工具**，
  对任何未知 opcode 都能生成 builder 映射。建议保留并参数化（按 type 过滤）。
- 本文档的"完整发送链路"小节是 00AB 的权威反汇编结论，可直接用于服务端模拟实现。

## 工具产物

- `ClientHack/plugins/0100_network_hook/network_hook.c`：
  - `cipher_encrypt_passthrough` / `cipher_decrypt_passthrough`：对 `packet_type==0xAB`
    dump 整帧 + 调用栈（→ `clienthack-00ab-capture.log`）。
  - `socket_write`：全量 Write trace（前 8 字节 + 调用栈 → `clienthack-write-trace.log`），
    message→builder 通用发现工具。
- `clienthack-00ab-capture.log`（游戏目录）：00AB 完整帧 + 调用栈。
- `clienthack-write-trace.log`（游戏目录）：所有 Write 调用的字段级 trace。

## 待办

- [ ] 业务语义确认：4 个槽位（tag 0..3）的 v1/v2 具体含义（需 DNF 业务知识或
      进一步反汇编对象填充逻辑 `0x3091FB8` 的写入点）。
- [ ] 服务端响应：客户端发完 00AB 后是否等待服务端 ACK？需观察发 00AB 与发
      GET_USERINFO(0x0008) 之间客户端是否阻塞（纯客户端侧可查：看触发函数
      `0x8C7E20` 是否在 00AB 后同步等待）。
- [ ] 把 00AB 结构补进 `NetworkingFrameProtocol.md` 的 Game 协议总表。
