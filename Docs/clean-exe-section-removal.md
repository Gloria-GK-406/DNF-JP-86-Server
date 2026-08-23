# Removing protector sections from the rebuilt client

Phase 6, step 1 of the clean-exe programme. Question: how much of the packed
image is dead weight that can simply be deleted, and how do we tell?

Result: **one section, 24.7 MB of 107 MB, is removable**, validated by a full
gameplay session. The rest is live and needs rewriting, not deleting.

Just as important is *how* that was established. The first three attempts all
produced wrong answers, and the reasons are reusable.

## What the packed file actually is

The shipped `DNF.exe` is 24.1 MB. Its section table declares 15 sections, but
only **three** of them exist on disk:

| # | name | RVA | VirtualSize | raw size | on disk |
|---|---|---|---|---|---|
| 0 | `.text` | `0x0001000` | 37.0 MB | 0 | no |
| 1 | `.code` | `0x23D3000` | 8.2 MB | 0 | no |
| 2 | `.data` | `0x2BAB000` | 21.6 MB | 0 | no |
| 3 | `.rdata` | `0x4045000` | 208 KB | 0 | no |
| 4 | `.X?9` | `0x4078000` | 986 KB | 0 | no |
| 5 | `.rsrc` | `0x416F000` | 64 KB | 0 | no |
| 6 | `.tls` | `0x417F000` | 2.1 MB | 0 | no |
| 7 | `.bss` | `0x4386000` | 16 KB | 0 | no |
| 8 | `.rsrc` | `0x438A000` | 7.6 MB | 0 | no |
| 9 | `.bss` | `0x4AC2000` | 36 KB | 0 | no |
| 10 | `.mackt` | `0x4ACB000` | 12 KB | 0 | no |
| 11 | `.,_W` | `0x4ACE000` | 3.9 MB | 0 | no |
| 12 | `.lA0` | `0x4E83000` | 208 KB | 208 KB | **yes** |
| 13 | ``.j`-`` | `0x4EB7000` | **24.7 MB** | **23.6 MB** | **yes** |
| 14 | `.rsrc` | `0x664D000` | 286 KB | 286 KB | **yes** |

Raw section bytes plus the 1024-byte header sum to exactly the file size, so
this is the whole story: sections 0-11 are reserved address space that the
protector inflates at runtime.

The original entry point, RVA `0x06176731`, lies **inside ``.j`-``**.

So ``.j`-`` is 98% of the shipped executable, and it is the shipping container:
the compressed and encrypted image of everything else, plus the Themida
unpacker that inflates it. `.lA0` holds the 86 real PE import slots the
unpacker needs before it can do anything, and `.rsrc` (14) stays unpacked so
Explorer can read the icon and version resources without executing the file.

Section names are not evidence. Themida randomizes its own (`.X?9`, `.,_W`,
``.j`-``, `.lA0`) and reuses ordinary ones as camouflage: there are **three**
sections called `.rsrc` and **two** called `.bss`, and only index 14 is a real
resource section. Even the client's own `.code` is characteristics `0xC0000040`
-- initialized data, not code.

## Startup, and which transformations can be undone

```
loader: map file, resolve 86 imports into .lA0, jump AEP 0x06176731 (in .j`-)
  |
  +-- (1) anti-debug / anti-VM probes
  |       0F 3F 07 0B (illegal opcode), ED = in eax,dx (privileged), each
  |       followed by sete storing the result
  +-- (2) self-decrypt
  +-- (3) decompress the payload into sections 0-11
  +-- (4) rebuild imports:
  |         - fill the client's own 406 IAT slots (at the start of .code)
  |         - build 431 wrapper sites (.,_W 419, .mackt 11, .tls 1) that store
  |           api_address + K for a per-site constant K
  |
  +-> mutated CRT in .,_W  ->  WinMain
```

The division that decides the ceiling of this whole programme:

| step | kind | undoable? |
|---|---|---|
| (3) decompression | **runtime** transform | yes -- a dump already holds the output |
| (4) import rebuilding | **runtime** transform | yes -- 406 slots redeclared as real PE imports, 431 sites fixed up by base delta |
| VM virtualization (vmp1) | **build-time** transform | no -- observing the runtime never yields the original code |
| CRT mutation (`.,_W`) | **build-time** transform | no -- must be re-synthesized |

Runtime transforms can be undone by observation. Build-time transforms cannot.
That is why steps 3-4 fell in weeks and the rest is a different project.

Note that ``.j`-`` is spent only because *our* rebuild never runs the AEP: the
rebuilt entry point is our own stub in `.chstub`, so steps 1-4 are unreachable
by construction, not merely unused in practice.

## How a section is dropped

`rebuild_runnable_pe.py --drop-sections <idx[,idx...]>`.

The section keeps a **placeholder header** with its original `VirtualAddress`
and `VirtualSize`, `SizeOfRawData = 0`, and characteristics `0x00000080`
(`CNT_UNINITIALIZED_DATA`, and *no* `MEM_READ`/`WRITE`/`EXECUTE`). The bytes
leave the file entirely; the loader maps the window `PAGE_NOACCESS`.

Two properties fall out of that choice:

* the window becomes a **complete detector**. Any access -- direct branch,
  indirect call through a runtime-computed pointer, exception handler, data
  read -- faults at a precise address. There is no way to touch the range and
  not be seen, and no way to touch it and silently corrupt something;
* **no other section moves**, so every surviving RVA -- every absolute pointer
  in the dump, every entry in `crossmodule-relocs.csv` -- is unchanged.

The placeholder cannot be omitted. The Windows image loader requires section
RVAs to be **contiguous**: each section must begin exactly where the previous
one ends, rounded up to `SectionAlignment`. A hole makes the PE fail validation
before a single instruction runs, with

> This command cannot be run due to the error: The specified executable is not a
> valid application for this OS platform.

which reads like an architecture mismatch and is nothing of the kind.

### The TLS directory has to move first

`IMAGE_TLS_DIRECTORY` lived at RVA `0x06230018`, inside ``.j`-`` itself. Only
the 24-byte descriptor is there -- its template (`.lA0`, RVA `0x04E83984`), its
index slot (`.data`, RVA `0x04042A2C`) and its (empty) callback list are all
elsewhere. The rebuilder appends a copy of the descriptor to `.idat2` and points
`DataDirectory[9]` at it. Verified no-op otherwise: rebuilding with the
relocation in place and nothing dropped changes exactly 16 bytes against the
previous build -- the directory RVA, `.idat2`'s `VirtualSize`, and the 12
non-zero bytes of the descriptor.

## Three wrong methods

### 1. Pointer density with the wrong source set

For each candidate section, count aligned DWORDs elsewhere whose value is a VA
inside it, and compare against a uniform-random expectation.

The first run excluded **every** protector section from the source set, on the
reasoning that they were all going away together. That reasoning is invalid the
moment the plan is to drop one section rather than all of them: sections 4-12
survive, so their references count.

The blind spot is not academic -- it is exactly what hid section 9's dependency.
With the corrected source set (all surviving sections), section 9 lights up:

| target | strongest source | ratio |
|---|---|---|
| **09 `.bss`** (required) | `.rsrc`(5), 4 hits vs 0.14 expected | **28.4** |
| | `.rsrc`(8) | 2.3 |
| **13 ``.j`-``** | `.bss`(9) | **1.26** |
| | every other surviving section | <= 1.07 |

Section 9 is the API-wrapper runtime's own data, referenced by *protector* code.
A static scan cannot see a dependency whose source it has already written off.

Section 9 also serves as the positive control that makes the clean result for
``.j`-`` meaningful: the method demonstrably detects a real dependency of the
same shape.

### 2. Reading the `.text` column

Even with a correct source set, the per-source breakdown matters. Every **data**
section scores 0.00 against every protector section -- there are no pointer
tables into any of them. All the apparent signal sits in the `.text` column, and
that column is worthless: instruction bytes are not uniformly distributed, so
the noise model does not hold. Read naively it ranked section 7 (23x) and
section 4 (13x) as the most heavily used and section 9 (1.56x) as free. All
three were wrong.

### 3. "The process is still running"

The first functional harness launched each build and reported whether it was
alive after 30-45 seconds. Every verdict it produced was wrong in at least one
direction:

* dropping section 6 scored **ALIVE** while the host had not initialized a
  single plugin -- the process existed with a dead startup path;
* dropping section 8 (7.6 MB of virtualized client functions) scored **ALIVE**
  on the same basis;
* dropping 9+13 scored **DIED** once and **RENDER** four times, because the
  harness's kill-the-previous-client step raced the next launch.

A surviving process ID is not evidence.

## What works

Run against the live S4A12 server and score by how far the client gets:

| verdict | meaning |
|---|---|
| `DEAD` | process gone, or the host never wrote its log |
| `startup-only` | plugins initialized, but no window ever appeared |
| `RENDER` | responding main window plus >20 s of CPU: CRT, imports, D3D, resource loading and the PVF parse all ran |
| `LOGIN` | the server also sent the character list |

`RENDER` is reachable in ~25 s unattended and exercises most of the startup
path. It is the bar a candidate must clear before it is worth a human session.

`LOGIN` is not yet automatable: the session token baked into the launch
arguments is not accepted by a freshly started server, so the client parks at
its own login screen. Obtaining a valid token would make the sweep fully
self-driving and is worth doing.

**`RENDER` is not acceptance.** It covers startup through the login screen and
nothing else. A build that clears it has earned a gameplay session, not a
verdict.

## Results

All builds from dump `dnf-dump-20260821-231752-744-73264`, scored with the
server running.

| dropped | saved | verdict | runs |
|---|---|---|---|
| -- (control) | -- | `RENDER` | 4/4 |
| **13** ``.j`-`` | **24.7 MB** | **`RENDER`** | **4/4** |
| 9 + 13 | 24.8 MB | `RENDER` 3, `startup-only` 1 | flaky |
| 9 | 36 KB | `startup-only` | 1/1 |
| 4 `.X?9` | 986 KB | `RENDER` 1, died 1 | flaky |
| 7 `.bss` | 16 KB | `RENDER` 1, died 1 | flaky |
| 5 `.rsrc` | 64 KB | died `0xC0000005` | 2/2 |
| 6 `.tls` | 2.1 MB | `DEAD`, plugins=0 | 2/2 |
| 8 `.rsrc` (vmp1) | 7.6 MB | `DEAD`, plugins=0 | 2/2 |
| 4,5,7,13 | 25.8 MB | died `0xC0000005` | 2/2 |

### Acceptance

Run `runs/20260822-091834`, played by hand against the upstream S4A12 server:

* **345 s, exit code `0x00000000`** -- normal quit, no fault;
* 353 captured frames, **106 distinct packet types** (34 sent, 72 received),
  covering movement, dungeon, quest and inventory traffic, not just login;
* ``.j`-`` mapped `PAGE_NOACCESS` throughout and never touched.

**107.5 MB -> 82.8 MB.**

Sections 4, 7 and 9 are *sometimes* fine, which is worse than being required:
they are touched conditionally, so a build that drops them clears the harness
and fails later in a way that looks like an unrelated bug. They stay.

Sections 5, 6, 8, 10 and 11 are live. Removing them means rewriting what depends
on them -- the API wrappers, the mutated CRT, and the virtualized functions in
vmp1.

## What to build next

The flaky rows are the lesson: section-granularity probing cannot answer "is
this touched" for anything touched conditionally, and it costs a launch per
hypothesis. Two different instruments are needed, and using the wrong one
wastes the run:

| target | instrument | why |
|---|---|---|
| ``.j`-`` | drop it and play | strong prior that it is dead; a fault would have been the most informative outcome available. **Done.** |
| 5, 6, 8, 10, 11 | `PAGE_GUARD` tracing | expected to be touched; a crash-based detector destroys the session it is trying to measure |

`PAGE_GUARD` tracing: map every protector section with `PAGE_GUARD` and install
a vectored exception handler. First touch of each page raises
`STATUS_GUARD_PAGE_VIOLATION` (`0x80000001`); the handler logs the faulting
address, the access type and the caller's EIP, then returns
`EXCEPTION_CONTINUE_EXECUTION` -- the kernel has already cleared the guard, so
the access proceeds and the client behaves normally. One gameplay session then
yields the live page set of every protector section at once, with callers, at
zero behavioural cost.

That measurement tells us which parts of `.,_W` are CRT versus wrapper, which of
the ~25 virtualized functions in vmp1 actually run, and transfers unchanged to
S4A21.

---

# PAGE_GUARD 测量结果（2026-08-22）

三次会话的并集：启动期（8 秒布防）、登录界面空闲 20 分钟（30 秒布防）、**真实对局
227 秒**（30 秒布防，329 帧 / 91 种封包，含移动与副本）。共 3794 条首次触碰记录。

| # | 节 | 大小 | 页数 | 触碰 | 活跃率 | 客户端调用点 | 谁在碰 |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 4 | `.X?9` | 986 KB | 247 | **0** | **0 %** | 无 | — |
| 5 | `.rsrc` | 64 KB | 16 | 3 | 18.8 % | 无 | 自身 |
| 6 | `.tls` | 2.1 MB | 519 | 162 | 31.2 % | **1** | 自身 223、vmp1 21 |
| 7 | `.bss` (vmp0) | 16 KB | 4 | 1 | 25 % | 无 | **vmp1 读取** |
| 8 | `.rsrc` (vmp1) | 7.6 MB | 1848 | **1210** | **65.5 %** | **83** | 自身 2082、`.text` 7 |
| 9 | `.bss` | 36 KB | 9 | **0** | **0 %** | 无 | — |
| 10 | `.mackt` | 12 KB | 3 | 3 | 100 % | 2 | 自身 |
| 11 | `.,_W` | 3.9 MB | 949 | 648 | **68.3 %** | **56** | 自身 1439 |

「客户端调用点」= 出错指令位于 `.text`/`.code`/`.data`/`.rdata`/`.lA0`/`.rsrc`(14)
的记录数。执行型缺页的 EIP 落在被保护页内，此时取栈顶返回地址作为调用点。

**这些计数全是下界**：每页只在首次触碰时报告一次，同页上的其它调用点看不见。

## 四个结论

### 一、vmp1 不是「约 25 个虚拟化函数」，而是客户端的核心代码

之前的静态估计（27 个前置 int3 填充的入口 / 42 个过滤后的分支目标）**低了一个数量
级**。实测 **1210 个活跃页、至少 83 个不同的客户端调用点**，且是下界。

已知它同时含两类东西：

* 真虚拟化代码——`0x0482D81D` 是标准 VMProtect 入口
  （`push imm32; push regs; jmp dispatcher`）；
* ~~原生可挂钩的客户端代码~~——**此项已被推翻，见下节。**

两者比例决定第 4 层的全部工作量。**在测出比例之前，讨论「反虚拟化 25 个函数」是无意
义的——那个数字是错的。**

### 二、vmp0 是 VMProtect 的字节码/数据节，符合预期

第 7 节唯一一条记录是 `0x04787C24` 被 `0x04CD0672`（vmp1 内）**读取**。解释器读字节
码，教科书行为。它只有 16 KB，跟着 vmp1 一起处理即可。

### 三、`.tls`（第 6 节，2.1 MB）是保护层内部机器

31.2 % 活跃，但触碰它的 223 次来自它自己、21 次来自 vmp1，**客户端调用点只有 1 个**。
它不是客户端依赖，是保护层运行时。移除它属于「拆保护层」而不是「改客户端」。

### 四、第 4、9 节从未被触碰，但删了会崩——盲区在启动门之前

`.X?9`（986 KB）和 `.bss`(9)（36 KB）在全部三次会话、含真实对局中**一页都没被碰过**，
可删除测试里第 9 节必崩、第 4 节时好时坏。

补充实验：把监视窄化到 4/7/9 并把布防提前到 500 ms，仍然只拿到第 7 节那一条。

原因是**布防最早也只能发生在 ClientHack 的启动门**，而门已经在变形 CRT 与最早期客户
端初始化之后。**这段盲区不是延迟参数能缩小的**，要覆盖它得换机制（例如在入口 stub
里布防，而不是在插件里）。

在此之前，对第 4、9 节只能说：**它们只在启动门之前被使用**。这与第 9 节是 API 包装
运行时数据、第 4 节是保护层早期初始化数据的推断一致，但未证实。

## 对计划的影响

原计划「② 包装器 → ③ CRT → ④ VM（可能不用做）」需要修正一处：

**第 4 层的规模是未知的，而且很可能远大于原估计。** 但方向没变——如果 vmp1 里多数是
原生代码，它不需要反虚拟化，只需要**留在一个普通 RX 节里**；保护层运行时（解包器、
反调试、API 包装、`.tls`）照样能彻底消除。

下一个该测的量：**vmp1 的 1210 个活跃页里，有多少属于 VM 入口/解释器，多少是原生
客户端代码。** 这是决定第 4 层做不做的唯一数字。

---

# vmp1 的构成（2026-08-22）

上一节留下的问题是「vmp1 里原生代码与虚拟化代码的比例」。答案：**几乎全是虚拟化的，
第 4 层不可行，vmp1 必须原样留下。**

## 一次失败的测法，以及它为什么无效

第一版判据是：取所有执行型缺页记录，若栈顶落在客户端节则视为「调用者 → vmp1 目标」
配对，再反汇编目标判断是 VM 入口还是普通函数。得到 112 个目标、62 % 判为 native。

**这个结果整个是假的**，两处根本错误：

* **缺页地址不是函数入口。** 执行型 guard fault 报的是「执行进入该页的位置」。对解释
  器而言那是分发器随机跳到的落点，与函数边界无关。
* **栈顶不是返回地址。** VM 代码运行在自己的虚拟栈上，栈顶偶然落在 `.text` 范围内不
  代表任何调用关系。

判据本身也有 bug：把
`push imm32; push ecx; push edx; push ebx; push eax; call <dispatcher>`
判成 native，因为它在前 14 条指令里看到了 `call`。VMProtect 的 VM 入口既有 `jmp` 形
式也有 `call` 形式。

留作教训：**先验证判据能认出已知样本，再看它的统计输出。** 这里若先拿已确认的
`0x0482D81D` 过一遍判据，一眼就能发现它把 VM 入口判成 native。

## 有效的测法：直接找分发器

VMProtect 的分发是

```
movzx eax, byte ptr [reg + <解码表>]
jmp   dword ptr [eax*4 + <句柄表>]
```

后一条的编码是 `FF 24 <SIB> <disp32>`，其中 SIB 的 scale=4、无 base。扫描 vmp1：

| 量 | 值 |
| --- | ---: |
| 分发点 | **520** |
| 各自独立的句柄表 | **520** |
| 分发点跨度 | `0x04798BF5` .. `0x04E0CF67` |
| 占节的比例 | **90.2 %** |

**520 个各自独立的 VM 实例，铺满整个 7.2 MB。** VMProtect 通常每个被虚拟化的函数发一
套分发器与句柄表，所以量级是**数百个虚拟化函数**，不是 25 个。

句柄表本身是短表且带混淆（`0x047BB27B` 只有前 5 项是合法 vmp1 地址，其后是代码字
节），没有继续深挖——那属于反虚拟化工程本身。

## 推翻一条此前的论断

此前用来论证「vmp1 含原生客户端代码」的证据是「NetworkHook 在 RVA `0x048FF9AB` 挂
socket-write staging，钩子正常工作」。反汇编该地址：

```
048FF9AB  push eax
048FF9AC  mov  eax, dword ptr [esp]
048FF9AF  xor  dword ptr [esp], eax
048FF9B2  pushfd
048FF9B3  call 0x48ff9b9
048FF9B8  jmp  0x48ff93b
```

**这是混淆过的 VM 入口，不是函数序言。** 钩子能工作只因为 MinHook 替换前 5 字节即可，
它不关心目标是原生码还是 VM 码。「钩子能用」推不出「是原生代码」。

## 调用密度旁证

| 区域 | 0 call 的页 | ≥3 call 的页 |
| --- | ---: | ---: |
| `.text`（确知的客户端代码） | 16 % | 82 % |
| `.,_W`（变形 CRT + 包装器） | 60 % | 29 % |
| vmp1 活跃页 | 45 % | 28 % |

vmp1 的形态贴近变形/解释器代码，远离正常编译产物。

## 结论

**第 4 层（反虚拟化）取消。** 理由：

1. 规模是数百个 VM 实例、7.2 MB，不是 25 个函数；
2. 32 位 VMProtect 2.x **没有可用的公开工具**（NoVmp 是 x64/VMP3；vmp2 已归档且作者
   自述 brittle；VMPredator 是 x64/VMP3.5 且无公开工具）；
3. **不需要做**——vmp1 执行完全正常，把它留在一个普通 RX 节里即可。

「干净 exe」因此定义为：**保护层运行时（解包器、反调试、API 包装、变形 CRT）彻底消
除，vmp1 作为不透明但正常工作的代码节保留。**

## 它挡不挡协议工作

不挡。协议关键路径都在 `.text`：

| 钩子 | RVA | 所在节 |
| --- | --- | --- |
| cipher-encrypt | `0x01C11360` | `.text` |
| cipher-decrypt | `0x01C11420` | `.text` |
| inbound read u8/u16/u32/bytes | `0x01D004xx` | `.text` |
| inbound set-remaining | `0x01CFF6A0` | `.text` |
| socket-write staging | `0x048FF9AB` | **vmp1**，但照样挂得上 |

唯一落在 vmp1 里的那个钩子实测工作正常。**vmp1 不构成 A21 协议开发的阻塞。**

---

# S4A21：还能丢掉哪些段（结论：只有容器段，这条线到此为止）

S4A21 的段布局与 S4A12 同构，13 号段 `.#|U`（26.6 MB Themida 容器）已由一局完整副本验证
可丢。剩下四个候选逐个查清了，**三个是活的**，净收益只有 16 KB。

## 先用离线证据筛，别用「丢了再跑」筛

跨模块重定位表本身就是判据：一个段里若有站点在运行期被当作模块指针修正，它就不可能是
解包后作废的容器。

| 段 | 大小 | 跨模块指针 | 判定 |
| --- | ---: | ---: | --- |
| `.eI4` | 1.05 MB | 0 | 候选 |
| `.vmp0` | 16 KB | 0 | 候选 |
| `.mackt` | 16 KB | **11** | **活的，不能丢** |
| `."2A` | 0.20 MB | **47** | **活的，不能丢** |

`."2A` 值得单独说：它在 dump 里 **99.9% 是零**（熵 0.02），看着像一段废弃的空白。但那
0.1% 的非零内容恰好是 47 个 DWORD 模块指针（47 × 4 = 188 B，与实测非零字节量吻合）——
它是一个**稀疏的 API 指针数组**。只看熵或非零比例会把它误判成可丢。

## `.eI4` 是活的，而且说明壳在运行期没有退场

带 `--drop-sections 4,7,13` 的构建启动 13 秒即崩。`0007_FaultRecorder` 给出了确切现场：

```
code=0xC0000005 at=0x05833BB0 access=read target=0x04AB6243
   at     -> 11:.-wI    （变形 CRT）
   target -> 04:.eI4    （被丢弃的段，PAGE_NOACCESS 占位符）
```

**变形 CRT 在运行期读 `.eI4`。** 这不是启动期的一次性解包引用，而是常驻依赖：那 1.05 MB
高熵数据（熵 7.96、99.5% 非零）是 `.-wI` 的运行期数据源。

这也是 `PAGE_NOACCESS` 占位符的价值：段被丢掉后残留的引用会在**精确地址**上触发故障，
而不是读到零然后在几百毫秒后以某种无关的形态崩掉。配合故障记录器，一次启动就定位到了
「谁读了哪一段」，不需要二分。

## `.vmp0` 可丢，但不值得

`--drop-sections 7,13` 启动通过（741 MB、游戏窗口、零故障记录）。但这只是**启动判据**，
而按本文档开头那条规矩，「可安全删除」要一局完整游戏才成立——为 16 KB 占用一局测试时间
不划算。客户端已换回经副本验证的 13-only 版本；`.vmp0` 的丢弃留作以后顺便验证。

## 这条线的总账

| | 大小 |
| --- | ---: |
| 已丢弃（`.#|U`，副本验证） | 26.6 MB |
| 可丢但未验证（`.vmp0`） | 16 KB |
| 证实为活（`.eI4` + `."2A` + `.mackt`） | 1.29 MB |

**继续按段丢弃已无收益。** 剩下的壳表面是 `.vmp1`（7.8 MB 虚拟化）、`.-wI`（3.75 MB 变形
代码）和它读的 `.eI4`（1.05 MB 数据），三者都属于「反虚拟化，否则免谈」那一类。

---

# 用控制流判定每个段是做什么的（S4A21）

## 先说一个会骗人的地方：段名是伪装，属性才是真的

| 段 | 名字暗示 | `Characteristics` | 实际 |
| --- | --- | --- | --- |
| `.code` | 代码 | `C0000040` | **不可执行**，是数据（含 216 个 IAT 槽） |
| `.data` | 数据 | `C0000040` | 数据（一致） |
| `.rsrc`(5) | 资源 | `E00000E0` | **可执行** |
| `.bss`(9) | 未初始化数据 | `E0000020` | **可执行** |
| `.tls`(6) | 线程局部存储 | `E00000E0` | **可执行，而且是 VM 的一半** |

我第一次统计「`.code` 的 call 边」时就踩了这个坑——那是在数据里扫字节。递归下降如果不按
可执行属性设围栏，会走进 `.data` 线性解码出十几万条虚构指令（一次直线跑了 171296 条）。

## 方法

从**已知真实执行的入口**出发：stub 跳进的 CRT 续点 `0x05D06329`。只跟随实际解码出来的
直接边（fallthrough / 直接 jmp / 直接 call），间接分支一律记为「墙」而不猜。60 万条指令
预算内得到段间边矩阵。

## 结果

```
from \ to      .text    .tls   .vmp1  .mackt    .-wI
.text          23816       1      18       -      65
.tls               9   12715    2760       3     168
.vmp1             58    3358   19378       1      82
.mackt             -       -       -       6     219
.-wI              49      47       4     434   30610
```

读法：

* **`.text`（44 MB）是自洽的真实程序。** 23816 条内部边，出边不到 100。壳没有把它掏空。
* **`.tls` + `.vmp1` 是同一个东西**——双向 6118 条边，两边各自内部还有上万条。`.tls`
  那 2.3 MB 不是线程局部存储，是 VMProtect 解释器的一半。所以 VM 的实际体量是
  7.8 + 2.3 ≈ 10 MB，不是之前认为的 7.8 MB。
* **`.mackt`（16 KB）是桩表。** 内部只有 6 条边：进去几条指令就跳回 `.-wI`（进 434 出 219）。
* **`.-wI`（3.75 MB）是变形代码层**，30610 条内部边，自成一体。
* **`.eI4`（1.05 MB）零控制流**，只被读——它是纯数据。配合丢段实验里
  `at=0x05833BB0(.-wI) read target=0x04AB6243(.eI4)` 的故障，可以确定它是 `.-wI` 的
  运行期数据源。

## `api + K` 到此为止：四个独立判据都是否定

`.-wI` 有 441 个、`.mackt` 有 11 个站点存着 `api_address + K`。想批量还原成命名 API，
试过四条路：

| 假设 | 结果 |
| --- | --- |
| 同模块的站点共享 K | user32 的 95 个站点 **95 个不同 K** |
| K 是站点地址的函数（±、^、返回地址、镜像基址…10 种） | **0 / 452** 落回模块范围 |
| 存在共享的解码 helper | 站点前是 `call`+内联常量的有 188 个，却对应 **186 个不同 helper**（fan-in 几乎全是 1） |
| K 是 64 K 对齐（低 16 位可匹配导出 RVA） | **6 / 452**，噪声水平 |

结构确实看清了——典型形态是 `call <helper>` 紧跟 4 字节内联常量，helper 靠返回地址取它。
但 helper 是**每站点各生成一份**的多态代码，没有共享变换可逆。**静态批量还原这条路是死的。**

唯一可行的是运行期观察（在站点上断下来看 helper 返回什么 API），那是逐站点的工作，
属于插桩范畴。

## 这轮分析真正产出的东西

`.text` 出边不到 100，意味着**真实程序交给壳的接口是一个可枚举的小集合**。想知道壳到底
拿走了什么，那份清单就是答案——而不是去啃 10 MB 的 VM 或 3.75 MB 的变形代码。
