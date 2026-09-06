# `.vmp1`：它确实是 VMProtect 虚拟机，我之前的否定结论错了

更新时间：2026-08-25

## 结论摘要

我此前根据"路径汇聚率只有 5%"断定 `.vmp1` **没有共享分派器、因而不是经典虚拟化**。
**这个结论是错的**，而且错在测了一个按设计就不该存在的东西：

这台 VM 是 **direct-threaded（直接线索化）**。字节码里存的**就是加了偏置的 handler 地址**，
每个 handler 自己 `jmp` 到下一个——**没有中心分发循环，所以本来就不该汇聚**。
"没有汇聚 ⇒ 没有 VM"这个推理不成立。

## VM 入口

41~53 份**逐字节相同**的克隆，每份 83~86 字节。典型一份在 `0x0528D018` / `0x051FD53D`：

```
push imm32                      ; 每函数密钥，由调用方压入
push ecx,edx,ebx,eax,esi / pushfd / push ebp,edi     ; 密钥落到 [esp+0x20]
call $+5 / pop eax / sub eax,self                    ; delta（自重定位，无重定位表）
mov edi, fs:[0x18] / mov edi,[edi+8]                 ; TEB → NT_TIB.StackLimit
<向下探页，直到 esp 上方有 16KB 已提交栈>
mov [edi], delta / mov [edi+4], delta                ; VM 上下文基址
mov esi, [esp+0x20]                                  ; 取密钥
rol esi, 9 / add esi, 0xBD81ACE6 / add esi, delta    ; → VIP（虚拟指令指针）
mov ebx,[esi] / add esi,4 / add ebx,0x4CC42998 / add ebx,[edi] / jmp ebx
```

至此 `esi` = 虚拟指令指针，`edi` = VM 上下文基址。

**端到端验证过**（不是推断）：

```
push 0xFEA3FB4A  →  VIP 0x057842E3  →  handler 0x0574E7A9
push 0x8D23D382  →  VIP 0x0528B200  →  handler 0x052B27DE
```

## 派发与 handler 集

```
派发     mov ebx,[esi] ; add esi,4 ; add ebx,0x4CC42998 ; add ebx,[edi] ; jmp ebx
偏置     +0x4CC42998（handler 侧）  +0x5B7DF9F4（派发器侧解码用）
```

用派发器偏置解码 `.vmp1` 全部 8,175,612 个 dword 位置：**166,253 个落回 `.vmp1`**
（2.03%，基率 0.190%，**10.7 倍**），其中 **158 个目标覆盖 90% 命中** → 有效 handler 集 ≈ 158。

emulation 拿到的 774 个 handler 地址里，**758 个在整个 122MB 镜像中从未以 dword 形式出现**，
也没有任何 ≥6 个的聚簇——**没有 handler 表**，这是 direct-threaded 的直接证据。

## 与 VMProtect 教科书结构的对照

19 条标准预测：7 条成立、3 条部分成立、8 条失败。**失败的方向一致，且是有信息量的那部分**：

| 预测 | 结果 |
| --- | --- |
| vm_entry 形态（push key / delta / rol+add 取 VIP） | **成立**，完全教科书 |
| 无重定位目录，VM 自带 delta 自重定位 | 成立 |
| handler 数量在 VMP 常见量级（≈158） | 成立 |
| VIP 单向前进 | 成立（3372 前 / 45 后，后者是 VM 分支） |
| **有 handler 表 + opcode 字节** | **失败**——直接线索化，字节码存的是地址 |
| **多态（每次编译不同 handler）** | **失败**——vm_entry 有 53 份逐字节相同的克隆 |
| **VM 局限在自己的节** | **失败**——不局限于 `.vmp1` |

## 保护强度分三档（实测）

不是所有 `.vmp1` 目标都被虚拟化：

- **入口窃取（entry stealing）**：13 个函数，首 5 字节换成 `jmp`，其余原样保留
- **mutation**：3,939 个游戏函数向 `.vmp1` 发出 10,725 条变异边
- **完整虚拟化**：1,673 个真 call 目标里 1,472 个确实不可读

## 三处我此前算错的数

**一、"2508 个入口"复现不出来。** 真实计数：call 目标 **1,673**、jmp-only **2,867**、并集 **4,268**、
过滤后 3,557。差异来源见下条。

**二、`functions.csv` 的 `span_bytes` 不是函数长度。** 它一路吃到下一行开头，
包含尾部 `int3` 填充——**0/67,073 例外**。VMProtect 恰好把游离 stub 停在那些填充里：
8,003 个 `.text → .vmp1` 引用点中，6,111 个在真实函数体内，**1,892 个在填充尾部**
（715 个目标只出现在那里）。

> 例：`ISocket::Recv` 表面上 `jmp 0x05790FE9`，但那个 `jmp` 在它的 `ret` 之后 8 个 `int3` 处，
> 是独立 stub，不属于该函数。

**三、"1499 条符号执行走到 800 条预算上限"——标签是错的。**
没有一条真的执行了 800 条（最长 85，全体最长 133）；预算提到 8000 输出逐字节相同。
它们全从 `break` 出去：**重访 1,493 / 出界 4 / 解码失败 2**。围栏违规 0。
那个"循环"就是 vm_entry 的探栈循环：

```
0x0542F88C  cmp ecx, 0x4000
0x0542F892  jg  0x0542F8A4      <- 唯一出口
0x0542F894  sub edi, 0x1000
```

**四、"`.vmp1` 大量读 `.data` 两页"——前提错了。**
`0x0399C000` 是 MSVC CRT/STL 静态数据块，热点 `0x0399CEE0` 是 `__security_cookie`：
全镜像 39,368 次引用里 **39,120 次打在这一个地址**，43.7% 的 `.text` 函数都引用它。
基率一算信息量为零。`0x03B89000` 是全零 BSS，位于一段 21.7MB 的连续零区里。

## 一条改变结论的方法学修正

> **对混淆块做线性扫描，测的主要是字节统计而非指令流。**

线性扫描让 `.vmp1` 显得富含 `aas/pushal/ljmp/loop*/jecxz/jp/cmpsb`——这些全是单字节操作码
（`0x3F/0x60/0xEA/0xE0-E3/0x7A/0xA6`），是把数据当代码解码的产物。

改用**从真实入口递归下降**（`.vmp1` 414,711 条 / `.-wI` 274,286 条）和
**symeval 实际执行过的 trace**（22,150 / 50,401 / `.text` 对照 31,609）之后：

| | `.vmp1` | `.-wI` |
| --- | --- | --- |
| `bt/bts/btr/btc`、`bsf/bsr`、`shld/shrd`、`xadd`、`bswap`、`neg` | **恰好 0 次** | 约 5% |
| in-degree == 1 占比 | 97.5% | 99.5% |
| 指令密度（字节/指令） | 2.99 | 2.77 |

**同一款壳的两个不同子系统，不是同一发射器换参数。** `symeval.py` 可以原样跑在 `.vmp1` 上，
但不该指望它产出 `.-wI` 那种收益——`.vmp1` 缺的是**函数清单**，不是去混淆。

## 未决

- handler 语义未还原（158 个有效 handler，只知道地址与派发方式）。
- 每函数密钥的来源未查（`push imm32` 是调用方给的，谁生成的、是否与 `.eI4` 相关未知）。
- `vmp1:q3` 那一问在额度耗尽时失败，未补。

## 相关文档

- `Docs/a21-client-cartography.md` —— 字符串解密与源码树归属
- `Docs/a21-protocol-surface.md` —— 747 个包类型
- `Docs/themida-api-wrapper-recovery.md` —— `.-wI` 那一侧的还原

## 细文档索引

本文是摘要。逐问的取证在 `Docs/a21-vmp1/`，按提问顺序：

- [`vmp1-0`](a21-vmp1/vmp1-0.md) —— What `.vmp1` actually is
- [`vmp1-1`](a21-vmp1/vmp1-1.md) —— .vmp1 convergence clusters 0x0542F873 and 0x054FEC24
- [`vmp1-2`](a21-vmp1/vmp1-2.md) —— .vmp1 and the two .data pages 0x0399C000 / 0x03B89000
- [`vmp1-3`](a21-vmp1/vmp1-3.md) —— The 2,508 `.vmp1` entries called from `.text` — how many are live?
- [`vmp1-4`](a21-vmp1/vmp1-4.md) —— why 1,499 walks into `.vmp1` "hit the 800-instruction budget"
- [`vmp1-5`](a21-vmp1/vmp1-5.md) —— .vmp1 — which entries deserve the CThread::Create / ISocket::Write treatment
- [`vmp1-6`](a21-vmp1/vmp1-6.md) —— `.vmp1` vs `.-wI` — structural comparison, and the symeval decision
- [`vmp1-7`](a21-vmp1/vmp1-7.md) —— `.vmp1` vs. the VMProtect 2.x/3.x layout playbook
