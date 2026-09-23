# A21 任务 grade 与中文分类：已证边界

2026-09-23，只读调查。对象为 `DNFClient-A21-debug` 的 `DNF-memory-rva.bin`（映像基址 `0x04000000`，对应 A21 `DNF.exe` SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`）、同客户端 `dstr.dat`（SHA-256 `60E9857773831B341BAE0D490FCAEAF0AF184B9776717E2FCF293E1BC7F92F10`）及当前 `Script.pvf`。RVA 均针对上述映像；不可套用旧版客户端地址。

## 直接证实的 UI 数值

中文 dstr 解码文件 `E:/Project/DNF/Document/dstr_res_66_plain.bin` 第 13442–13449 行给出 `48100 重要任务`、`48101 主线`、`48102 普通`、`48103 成就`、`48104 每日`、`48105 重复`、`48106 系统`、`48107 外传`。这只证明词条存在，不自动证明对应 PVF grade。

在 A21 任务 UI 构造函数 `RVA 0x1F72979–0x1F72BDD`，同一段先把数值交给 `0xAC2700`，再用 dstr ID 构造标签，可直接配对：

| 内部数值 | 中文 UI 标签 | 关键 RVA | 置信度 |
| --- | --- | --- | --- |
| `0` | `48101 主线` | `0x1F72979`、`0x1F729AF` | Confirmed：静态构造路径 |
| `2` | `48103 成就` | `0x1F72B37`、`0x1F72B6D` | Confirmed：静态构造路径 |
| `3` | `48104 每日` | `0x1F72BA6`、`0x1F72BDD` | Confirmed：静态构造路径 |
| `12` | `48106 系统` | `0x1F72A58`、`0x1F72A8E` | Confirmed：静态构造路径 |
| `13` | `48107 外传` | `0x1F72AC4`、`0x1F72AFE` | Confirmed：静态构造路径 |
| `19` | `67008 异界` | `0x1F729E8`、`0x1F72A1F` | Confirmed：静态构造路径 |

另一条任务提示文本路径 `RVA 0x121E79B` 读取任务对象 `+0x9C` 原始 grade；其分支将 `0` 显示成 `6562 主线`、`2` 显示成 `6563 成就`、`5/7/8` 显示成 `6560 支线`。`dstr_res_66_plain.bin` 第 3226–3229 行验证这些词条。这里的“支线”不等于上述分类栏的“普通”或“外传”，不能跨 UI 路径互换。

**`48102 普通` 的数值绑定尚未找到。** 对该 dump 全文件搜索小端 `48102`（`E6 BB 00 00`）只命中 `RVA 0x258B3D9` 一处；从 `0x258B3C0` 对齐反汇编，它位于 `RVA 0x258B3D7: je 0x2596FC3` 的相对位移中，并非 dstr 调用。故现有静态证据不能断言“普通”是某个单独 grade，或由哪几个 grade 合并筛选；词条存在也不能证明当前界面必用它。

## PVF token 到 UI 数值的断点

当前 PVF 清单首个 `[grade]` 值的全量统计见 [grade-pvf-audit.md](grade-pvf-audit.md)：`[epic]` 757、`[side]` 180、`[common unique]` 275、`[achievement]` 466；没有 `[normal]`。`[side]` 中有永久属性奖励，`[common unique]` 中有转职及其他特殊奖励。该统计是 PVF 定义事实，并非客户端分类映射。

A21 客户端 PVF 任务解析器 `RVA 0x24D0F80–0x24D183E` 确实把内部 token ID 分支映射成任务对象 `+0x9C` 的 `0..22` grade 数值；关键跳表在 `0x24D16CC–0x24D16E1`，其中 `0x24D16F7` 写 `0`、`0x24D1702` 写 `2`、`0x24D17D4` 写 `13`。但此分支的输入是解析器 token ID（从对象 `+0x10` 读取），尚未把 PVF 字符串 `[side]`、`[common unique]`、`[epic]`、`[achievement]` 精确对应到该 ID。`PvfProbe tokens` 展示的字符串池偏移不是该枚举 ID，不能直接代入跳表。因而仅靠目前静态材料，**不能证明 `[side]` 或 `[common unique]` 哪个是“普通”，也不能证明 `[side]` 必然是“外传”**。UI 数值 `13 = 外传` 已证，但对应的 PVF token 未证。

`[epic] → 主线` 有任务名称、PVF 语义和既有角色样本的强旁证；本轮最严格的静态链仍缺 token→内部 ID 的一步，标为 High 而非独立 Confirmed。`[achievement] → 成就` 同理。券文案“主线+普通”不能据英语 token 字面翻译成 `[epic]+[side]`，也不能把含转职线的 `[common unique]` 整类纳入。当前可安全落到 PVF 的严格集合只有已另行取证的主线候选；“普通”集合应保持未定，待客户端 token 表、实际任务 UI 样本或券成功前后 quest bitset 差分补证。

本报告没有修改客户端、PVF、数据库或运行进程。
