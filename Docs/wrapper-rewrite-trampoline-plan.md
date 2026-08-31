# 阶段 2 施工方案：5 字节跳桩

日期：2026-08-31。前置：`Docs/wrapper-eligibility-census-results.md`（资格普查结果）。
产物：`ClientHack/tools/wrapper-rewrite-plan.csv`、`ClientHack/tools/build_rewrite_plan.py`。

## 一句话

把 9-10 字节的约定模板从入口挪到分配出来的桩上，入口只留 5 字节 `E9 rel32`。
施工清单从 **640 扩到 1158**（+518），并顺带修掉 29 个会打进隔壁入口的条目。
已实机验证：A、B 两种约定均在跳桩下跑通且有执行证明。

## 一、原来卡在哪

`check_eligibility_results.py` 的合格判据是 `safe_len >= 10`(A) / `>= 9`(B/D)，
因为模板要写在入口原地：

```
A (esp+4/R)   pop eax; mov [esp],eax; push imm32; ret   = 10 字节
B (esp+0/R+1) inc [esp];              push imm32; ret   =  9 字节
```

521 个判定为真转移型的入口过不了这一关：

```
safe_len 分布  6 → 389    7 → 105    8 → 19    9 → 6    5 → 1    3 → 1
分组           A 283      B 238
```

389 个卡在 `safe_len=6` 不是巧合——正是 `themida-api-wrapper-recovery.md` 记载的
「入口头部 ~6 字节是按调用方定制的栈修整，+6 是共享的已修整入口」现象：
有外部控制转移直接落在 +6。

## 二、顺带查出的一个真 bug

`safe_len` 只编码**前缀落点**约束（`build_eligibility_worklist.py` 取
`min_external_offset`）。它**不包含**「下一个命名入口的间距」——
`generate_wrapper_pilot.py` 是用独立的 `GAP_MIN = 11` 管那个的，
而 `check_eligibility_results.py` 的 `eligible` 公式**从未检查间距**。

后果：**640 个"合格"入口里有 29 个的下一个命名包装器入口就落在补丁覆盖区内**
（gap = 2~8，模板需要 9~10）。它们 `safe_len=16` 所以前缀检查全过。
照原清单施工会覆写掉隔壁包装器的入口字节。

```
0x05AF34EF B msvcp60!assign        gap=2   needs 9
0x059DCFB5 B shell32!SHGetSpecial  gap=6   needs 9
0x05A9568A A user32!SetCursorPos   gap=6   needs 10
...共 29 个
```

`build_rewrite_plan.py` 把两个约束合并为 `patch_budget = min(safe_len, next_entry_gap)`，
这 29 个里 28 个改走跳桩即可，只有 `0x05AF34EF`（gap=2）无解。

## 三、方案

入口写 5 字节 `E9 rel32` 跳到桩，桩上是原来的模板：

```
入口:  E9 <rel32>                          (5 字节)
桩:    <约定修整> ; push <API 绝对地址> ; ret   (9-10 字节)
```

`rel32 = stub - (entry + 5)`。

**语义完全等价**：模板是位置无关的——`inc [esp]` / `pop eax; mov [esp],eax`
不引用自身地址，`push imm32; ret` 用的是运行期解析的 API 绝对地址
（不是 rel32，所以 app-local DLL 每次启动重定位也不受影响）。挪到哪里执行都一样。

桩池：`VirtualAlloc` 一块 `PAGE_EXECUTE_READWRITE`，每桩 16 字节槽位，
上限 1536 个（覆盖 1158 个清单条目还有余量），槽位其余字节填 `0xCC`。

## 四、结果

```
施工清单  1158 个合格  (inline 610 + trampoline 548)
  A 组    inline 299 + trampoline 296 = 595   ← A 组 VERIFIED 全覆盖
  B 组    inline 311 + trampoline 252 = 563   ← 565 个里 2 个几何预算 < 5
```

对照原来的 640，**净增 518**。仍被排除的 496 个：

```
  verdict ODD              295   （多为 caller 不可读单元）
  verdict MISMATCH         149
  verdict REGISTER-LOADER   49
  几何预算 < 5               2   0x05AF34EF (gap=2), 0x05CB49B8 (safe_len=3)
  D 组排除                   1
```

跳桩预算分布：`6 → 411, 7 → 110, 8 → 20, 9 → 6, 5 → 1`。

C 组与 D 组不进清单：C 组是实机证伪的装载桩组；D 组经六轮实机会话确认排除
（见 `fleet-wrapper-eligibility-workflow.md` 第十一、十二节）。

## 五、实机验证（已完成）

`0150_WrapperPilot` 新增 `Trampoline=1`，与 `WitnessThenRewrite=1` 组合使用，
可在**同一次调用里既证明调用点执行、又运行跳桩改写后的代码**：

```
pilot armed: 12/12 entries trapped for witness
WITNESS HIT gdi32.dll!SetTextColor   entry 05BDAB0A -- call site DID execute;
            wrapper NOW REWRITTEN as transfer (conv 1, 5-byte E9 trampoline)
WITNESS HIT user32.dll!GetClientRect entry 05B563B7 -- call site DID execute;
            wrapper NOW REWRITTEN as transfer (conv 0, 5-byte E9 trampoline)
```

客户端存活 105 秒，**首次异常 0 次、控制流异常 0 次**。A、B 两种约定均已验证。

## 六、复现

```ini
[WrapperPilot]
Enabled=1
Conventions=AB
Trampoline=1          ; 5 字节 E9 + 桩，替代原地模板
Witness=1             ; 可选：int3 一次性探针，提供执行证明
WitnessThenRewrite=1  ; 可选：命中即改写，同一次调用运行改写后代码
```

必须用 B1 未打包 exe（`CLIENTHACK_B1_MODE=1` + `DNF.unpacked-b1-debug.exe`）——
打包版上所有入口都是 `ERROR_ACCESS_DENIED`。

生成施工清单：

```
python ClientHack/tools/build_rewrite_plan.py \
    --results  ClientHack/tools/wrapper-eligibility-results.csv \
    --worklist ClientHack/tools/wrapper-eligibility-worklist.csv \
    --wrappers ClientHack/tools/wrapper-apis-a21.csv \
    --out      ClientHack/tools/wrapper-rewrite-plan.csv
```

## 七、待办

1. 全量 1158 个入口的样本表生成（现有 `generate_wrapper_pilot.py` 只出 20 个手挑样本，
   需要改成从 `wrapper-rewrite-plan.csv` 生成，并按 `patch_mode` 选 inline / trampoline）。
2. 分批实机验证：建议按模块分批 + `WitnessThenRewrite` 取得执行证明，
   不要一次全开——第六轮的教训是「登录界面存活」不足以支撑结论。
3. 桩池目前每桩 16 字节固定槽位；若清单继续增长需调 `WRAPPER_PILOT_STUB_MAX`。

## 八、全量表落地与可达性图谱（08-31 已完成）

### 8.1 全量样本表

`tools/generate_wrapper_pilot_full.py` 从 `wrapper-rewrite-plan.csv` 生成
`plugins/0150_wrapper_pilot/wrapper_pilot_full.h`：**1158 条**，
另带一个平行数组 `WRAPPER_PILOT_FULL_MODE[]` 记录每条的补丁模式
（0=inline，1=trampoline），插件按它逐条选择，`Trampoline=1` 可整体覆盖。

生成器刻意做成纯转录，不做任何过滤——**资格判定只在 `build_rewrite_plan.py` 一处发生**。

### 8.2 DryRun：1158/1158 就绪

新增 `DryRun=1`：执行全部检查（模块是否加载、导出是否解析、入口字节是否仍与 dump 一致）
但**一个字节都不写**，用于在冒任何风险之前测量覆盖率。

```
DRY RUN over 1158 entries: ready 1158 | module not loaded 0
                         | export missing 0 | entry bytes differ from dump 0
```

原本担心 shell32 / wininet / oleaut32 / combase 这些晚加载模块在启动门那一刻
解析不到，实测 B1 环境下全部就位，无一遗漏。

### 8.3 可达性图谱：194/1158（16.8%）

全量 1158 个入口开纯见证跑一局完整会话（登录→副本通关→多界面浏览→退出）：

```
  1158/1158 入口武装成功，会话全程无异常
  194 个入口的调用点确实执行  (16.8%)
```

按模块（执行数 / 清单数）：

```
  ntdll      7/10  (70%)    user32    82/338 (24%)    gdi32     10/57 (18%)
  oleaut32  15/22  (68%)    advapi32  14/55  (25%)    shell32    7/57 (12%)
  shlwapi    6/19  (32%)    msvcp60   43/519 ( 8%)
```

按象限分布均匀：A-inline 56、A-trampoline 40、B-inline 50、B-trampoline 48
——**一次分批验证即可同时覆盖两种约定与两种补丁模式**。

结果已标注回 `wrapper-rewrite-plan.csv` 的 `reached_ref_session` 列。

### 8.4 这个数字对阶段 2 的含义

**一次典型会话只能触及施工清单的约 17%。** 直接后果：

- 实机验证的上限就是这 194 个（换会话画像可以再多一些，但量级不变）；
- 其余约 964 个入口**永远不会被普通游玩验证到**，它们的正确性完全依赖普查的
  字节形状判定——这正是普查坚持「逐字节引用、零幻觉、对抗复核」的意义所在；
- 「没崩」对未执行的入口依然是空结论。这与 11.2 节（原试点 16 个里 13 个从未执行）
  和 12 节（我自己据「登录界面存活」下早结论）是同一条教训的第三次出现。

**建议的推进方式**：先用 `reached_ref_session=YES` 的 194 个做实机验证
（`FullTable=1` + `Witness=1` + `WitnessThenRewrite=1`，一次一批模块），
验证通过后再按证据放行其余部分，并在文档中明确记录哪些入口是「实机验证」
哪些是「仅证据支持」。

## 九、inline 模式撤回（08-31 实机推翻）

第一批实机验证（user32，338 个入口武装，按计划的 per-entry 模式）**进频道即崩**：

```
0xC000001D 非法指令 at 0x0591AB75   (改写 59 个之后)
```

故障地址在 `.-wI` 段内，但**比第一个命名包装器入口还低 500KB**——落在
`resolve_wrappers.py` 未能命名的包装器区域，不是我们改写的任何入口。

同一批只改一个变量（`Trampoline=1` 强制全跳桩）重跑：

| 批次 | 补丁模式 | 改写并执行 | 结果 |
| --- | --- | --- | --- |
| 1a | 按计划（多数 inline） | 59 | **进频道即崩** |
| 1b | 全部 5 字节跳桩 | **84** | 完整会话通关退出，**零可归因故障** |

1b 剩下的 3 个 `0xC0000005` 与**毫无改写**的纯见证会话逐条对应
（`abs:` 基址因 ASLR 不同，偏移与 `0x3C` 目标完全一致），是基线噪声。

### 9.1 根因：`next_entry_gap` 看不见未命名包装器

`build_rewrite_plan.py` 的 `next_entry_gap` 是在 `wrapper-apis-a21.csv`
（**命名**入口，1654 个）上算的。`.-wI` 段里还有大量未命名包装器，
对这个检查完全不可见。inline 要 9-10 字节、跳桩只要 5 字节——
多要的那 4-5 字节正好够覆写掉一个看不见的邻居入口，
而故障地址恰好就落在未命名包装器区域。

**结论：inline 模式撤回，全量 1158 个入口统一走跳桩。** 省下的 4-5 字节
不值得这个风险面，而且 610 个 inline 条目里到底有多少贴着未命名邻居，
在没有未命名包装器完整清单之前无从判断。

### 9.2 计划状态

```
eligible 1158  →  全部 trampoline  (A 595 / B 563)
  reached in a reference session : 200
  live-validated (改写并执行过)   :  84   (A 41 / B 43)
```

新增两列：`reached_ref_session`（参考会话中调用点是否执行）、
`live_validated`（是否被改写且执行过并存活）。
**这两列是「实机验证」与「仅证据支持」的分界线**，阶段 2 施工时应逐条记录。

剩余「可达但未验证」116 个：msvcp60 43、oleaut32 15、advapi32 14、gdi32 10、
ntdll 7、shell32 7、shlwapi 6、user32 4。

## 十、分批实机验证完成（08-31）

三批全部通过，全部为 5 字节跳桩、`WitnessThenRewrite` 模式
（每个入口在第一次执行的那一次调用里被改写，随即运行改写后的代码）。

| 批次 | 范围 | 武装 | 改写并执行 | 结果 |
| --- | --- | --- | --- | --- |
| 1a | user32，按计划的 per-entry 模式（多数 inline） | 338 | 59 | **进频道即崩** |
| 1b | user32，全跳桩 | 338 | 84 | 完整会话通过 |
| 2 | msvcp60，全跳桩 | 519 | 43 | 完整会话通过 |
| 3 | **全量计划**，全跳桩 | **1158** | **197** | **完整会话通过** |

批次 2、3 的首次异常与**毫无改写**的纯见证会话逐条对应
（两对 `VMXh` 反虚拟机探测 + `0x3C` 模块探测），无一可归因于改写。

### 10.1 最终状态

```
  合格清单        1158   (A 595 / B 563)，全部 5 字节跳桩
  可达             203
  实机验证         202   (A 98 / B 104)
  仅证据支持       956
```

实机验证覆盖 14 个模块：user32 90、msvcp60 43、oleaut32 15、advapi32 14、
gdi32 10、ntdll 7、shell32 7、shlwapi 6、combase 3、comctl32 2、winmm 2、
wininet 1、ole32 1、az 1。A、B 两种约定均有近百个入口的实机证据。

### 10.2 「仅证据支持」的 956 个

这 956 个入口的调用点在四局完整会话中**一次都没有执行**，因此不可能有实机证据——
这不是验证做得不够，而是包装器与调用点一对一、多数调用点属于冷路径的结构性事实
（可达率约 17%，与原试点 16 个样本里只有 3 个执行过的比例一致）。

它们的正确性完全依赖普查的字节形状判定。计划里的 `live_validated` 列
把这条界线显式记录下来，阶段 2 施工时应当保留该区分，
而不是把 1158 个入口当作同等置信度对待。

## 十一、「仅证据支持」的 956 个：代表性检验

实机验证只能覆盖可达入口，剩余 956 个永远跑不到。但**「跑不到」不等于「不同类」**：
如果已验证的 202 个在结构上就是从 1158 里随机抓的一把，那么实机证据的效力
可以外推——同一区段、同种类型的包装器不太可能使用不同的混淆模式。

这是可检验的。置换检验（从 1158 中随机抽 202，重复 1000 次，比较总变差距离）：

| 指标 | 实测 TVD | 随机抽样中位数 | p |
| --- | --- | --- | --- |
| 头部前 3 条指令（混淆模式的直接度量） | 0.276 | 0.243 | 0.097 |
| 头部前 5 条指令 | 0.760 | 0.773 | **0.809** |
| 约定 | 0.035 | 0.025 | 0.416 |
| `safe_len` | 0.067 | 0.065 | 0.467 |
| 机械续行点提示 | 0.024 | 0.040 | 0.802 |
| 对齐收敛点 | 0.164 | 0.119 | 0.035 |

**六项中五项与随机抽样无法区分。** 收敛点那项 p=0.035，但 6 次检验的
Bonferroni 阈值是 0.0083，撑不住多重比较校正。

**结论：已验证的 202 个是全体 1158 个的代表性样本**，实机证据可以外推。

### 11.1 混淆形态覆盖

| 形态粒度 | 已验证形态数 | 仅证据形态数 | 无同款可参照的条目 |
| --- | --- | --- | --- |
| 头部前 2 条指令 | 6 | 6 | **0 / 956 (0%)** |
| 头部前 3 条指令 | 51 | 99 | 125 / 956 (13.1%) |
| 头部前 4 条指令 | 122 | 410 | 490 / 956 (51.3%) |

深度 2 是**零孤儿**——每个未验证入口的开头两条指令都能在已验证集里找到同款。
深度 3/4 的孤儿率是**粒度假象**：已验证的 202 个在深度 4 上就有 122 种形态，
接近一条一形态，此时「没见过」不携带信息。深度 3 的孤儿形态
（`pop call dec` / `pop call bswap` / `pop call clc` / `pop call xchg`）
只差第三条指令，全是混淆链对折叠常量做的垃圾算术，前两条 `pop call` 是共享的。

### 11.2 真正的残余：12 个入口

按 (约定 × 机械续行点 × tight/clean) 分格，12 个格子里已验证集覆盖 11 个。
未覆盖的那格是 **`(esp+4/R, resume@+6, clean)`**，共 12 个入口：

```
0x05AC4DD6 advapi32!RegOpenKeyExA          0x05C1BDCC awesomium!WebCore::instance
0x05AEA8B1 user32!GetWindowThreadProcessId 0x05C1F392 user32!SetFocus
0x05B1CFC4 oleaut32!SysFreeString          0x05C3108B msvcp60!_Lockit::_Lockit
0x05BE1B4C msvcp60!_Lockit::_Lockit        0x05C6A531 msvcp60!_Lockit::_Lockit
0x05BFD5D4 msvcp60!basic_string::_Grow     0x05CD76B8 msvcp60!basic_string::_Split
0x05C03CEA shell32!ShellExecuteW           0x05D2AC14 shell32!ShellExecuteW
```

这一格的含义是：**分组为 A（约定预测 +5 续行），但机械打分器判 +6** ——
也就是普查 agent **逆着机械先验**做判断的那批。例如 `0x05C1F392`（SetFocus）
agent 的理由是「+6 需要一条荒谬的 `add esp, dword ptr [ebx+0x5f]`，故取 +5」。

**这 12 个是全清单里置信度最低的子集**：判断本身是逆先验的，且无任何实机证据支撑。

### 11.3 建议

- 其余 **1146 个**：代表性检验支持外推，可按证据放行。
- 这 **12 个**：要么排除（代价仅占清单 1%），要么定向触发验证——
  其中 `ShellExecuteW`（点外部链接）、`RegOpenKeyExA`（设置项读写）、
  `SetFocus`/`GetWindowThreadProcessId`（窗口焦点切换、Alt-Tab）
  都是可以人为触发的路径，一局定向操作即可覆盖大半。
