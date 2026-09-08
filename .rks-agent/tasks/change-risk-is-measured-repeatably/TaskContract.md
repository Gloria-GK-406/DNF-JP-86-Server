---
kind: rks-task-contract
revision: 2
state: ready
---

# TaskContract: 变更风险可以被反复量出来

## Context

### Source Request

所有者原话（照录，不改）：

> 看看在新的分支创建一下可持续测量的crap系统吧。最后产出在git仓库不追踪的路径。按日期时间命名。用sdd在新的worktree完成任务

随后在两个澄清问题上定了边界：

- **目标仓库**：`DNF-JP-86-Server`（外层 `E:/Project/DNF` 不是 git 仓库，另两个候选
  `ServerS4A21` 只有内嵌 `SelfTests`、`S4A21ClientPatch` 是无测试的 C++，都不选）。
- **语言范围**：**只做 C# Server**，同仓的 C 语言 `ClientHack/` 不纳入。

### Current Situation

仓库 `E:/Project/DNF/DNF-JP-86-Server`，源 checkout 在 `main`，HEAD
`addbeb52`，工作区干净。任务 worktree：
`.worktrees/change-risk-is-measured-repeatably`，分支同名，自 `addbeb52` 开出。

#### 判据基线（本轮在 `addbeb52` 实测）

命令：
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

结果：**2392 通过 / 2 跳过 / 0 失败**，退出码 0。逐项目：
Application 1070、Protocol 612、Persistence 333、Infrastructure 259(+2 跳过)、
Pvf 60、Networking 52、Host 6。

那 2 个跳过是 `A_missing_archive_*`，只在档案不可达时才跑，**跳过正是「档案确实
加载了」的标志**。`Script.pvf` 存在（72.9 MB）。

**跑判据前必须确认没有 `Dfo.Server.Host` 进程在跑**——运行中的 host 锁住
`Dfo.Server.Host/bin` 下的 dll，构建以 MSB3027 失败，看起来像判据坏了其实不是。
（本轮开工前已确认无该进程。）

#### 被测面

`Server/86JP.Server.slnx` 是完整宇宙：10 个 src 项目 + 7 个 test 项目。
`Server/src` 共 456 个 `.cs`、59506 行。逐项目（不含 bin/obj）：

| 项目 | 文件 | 行 | 有对应测试项目 |
|---|---:|---:|---|
| Dfo.Server.Protocol | 147 | 17997 | 是 |
| Dfo.Server.Application | 102 | 13546 | 是 |
| Dfo.Server.Abstractions | 110 | 11085 | **否** |
| Dfo.Server.Infrastructure | 36 | 6936 | 是 |
| Dfo.Server.Persistence | 20 | 5751 | 是 |
| Dfo.Server.Data | 12 | 1748 | **否** |
| Dfo.Pvf | 11 | 1350 | 是 |
| Dfo.Server.Networking | 16 | 993 | 是 |
| Dfo.Server.Host | 2 | 100 | 是 |
| Dfo.Server.Utilities | 0 | 0 | 占位空项目 |

`Server/tests/Dfo.Server.Abstractions.Tests`、`Dfo.Server.Utilities.Tests`、
`Integration/` 是**空目录，没有 csproj**，不在解决方案里，不构成测试入口。

#### 现状缺口

- 仓内**没有任何覆盖率或复杂度工具**：无 coverlet、无 `dotnet-tools.json`、
  无 `.github/` CI。`grep` 命中的只是 `Microsoft.NET.Test.Sdk` 传递引入的
  `Microsoft.CodeCoverage` 构建产物，不是被采用的配置。
- 因此**没有人知道这 59506 行里变更风险集中在哪**。改 Protocol/Application 这类
  大项目时只能凭印象判断「这块有没有网兜着」。
- `.gitignore` 里**没有 `.crap`**。`.worktrees/` 走的是 `.git/info/exclude`（本地
  排除，不随仓库分发）。
- `Tool/` 是本仓既有的独立工具位（`PvfProbe`、`PvfProxy` 是 .NET csproj，
  `WireSmoke` 是脚本集），**都不在 `86JP.Server.slnx` 里**，README 与 AGENTS.md
  都记着这个约定。

#### 构建约束（`Server/Directory.Build.props`）

net10.0、ImplicitUsings、Nullable、**TreatWarningsAsErrors**、Deterministic；
中央包管理（`Server/Directory.Packages.props`）；SDK 由 `global.json` 钉在
10.0.100 / rollForward latestMajor，本机实测 `dotnet --version` = 10.0.302。

## Goal

本仓有一套**装在项目里、可反复跑**的 CRAP 变更风险测量系统：任何时候由维护者跑一条
记录在案的命令，就能在 `.crap/<日期时间>/` 下得到该次快照的**方法级** CRAP 结果、
分布聚合与可读报告；历次运行各自独立留存、互不覆盖；这些产出不进 git 追踪。
并且已经用它跑出**一次真实基线**，说清当前风险集中在哪、以及有哪些有证据支撑的改进选项。

## Scope

### In

- 一个持久化、项目原生的采集系统：实现与钉住的配置留在仓库内稳定路径，运行结束后仍在。
- 一条记录在案的入口命令，从新 shell、在仓库根即可跑通。
- 复杂度与覆盖率的采集、函数级对接（join）、CRAP 计算与证据状态标注。
- 每次调用在仓库根 `.crap/` 下新建一个按本地日期时间命名的运行目录，自描述、不可变。
- 让 `.crap` 不被 git 追踪。
- 类 / 文件 / 项目 / 全域四层函数分布聚合。
- 一份人可读报告：证据有效性 → 分布形态与风险集中 → 排序后的发现 → 改进选项 → 局限。
- 至少两次调用的验证证据（两个并存的时间戳目录）。
- 在 `Document/` 留一份让维护者能独立复跑与解读结果的说明。

### Out

- `ClientHack/`（C 语言）与 `Tool/` 下的独立工具，不纳入被测面。
- 实际去重构或补测任何热点代码——本任务只产出选项，不动生产代码行为。
- CI 接线、定时任务、历史趋势库、跨提交对比报表。
- 修改既有测试用例或既有生产代码来抬高覆盖率。
- 另两个仓库（`ServerS4A21`、`S4A21ClientPatch`）。

## Constraints

- CRAP 只在**方法/函数粒度**计算，公式 `C²·(1−Cov)³ + C`，复杂度与覆盖率必须来自
  **同一份代码快照**。类、文件、项目只有分布，没有「一个 CRAP 分数」；不得把复杂度或
  覆盖率先聚合再套公式。
- 覆盖率缺失、映射歧义、被排除三种情况必须与「实测零覆盖」区分开，**不得静默填 0**
  进入实测分布。
- 运行目录名不得含冒号（Windows 拒绝），须字典序可排；同秒冲突要有确定性区分。
- 不得覆盖或复用既有的时间戳目录；失败的运行也要留下目录与状态。
- 新增的 .NET 工程必须服从仓库既有约束：net10.0、Nullable、
  **TreatWarningsAsErrors**、中央包管理。
- 跑测试需要 `PVF_ARCHIVE_PATH`；跑之前确认无 `Dfo.Server.Host` 进程。
- 装 CRAP 不得改变被测生产代码的行为，也不得让判据基线（2392/2/0）变差。
- 采集系统本身不得只存在于临时目录——临时目录只能放缓存和中间产物。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 采集系统在仓库内的稳定路径上，删掉所有临时/构建目录后仍能从新 shell 跑通 | 记录在案的入口命令 + 清掉中间目录后从新 shell 复跑成功的输出与退出码 |
| SC-2 | 一次调用产生一个新的 `.crap/<本地日期时间>/`，第二次调用产生并存的兄弟目录，第一次的内容原样保留 | 两次调用后的目录列表，以及第一次运行产物的前后一致性比对 |
| SC-3 | `.crap` 不被 git 追踪 | 在有产物的情况下 `git status --porcelain` 不出现 `.crap`，且 `git check-ignore -v .crap/...` 指出排除来源 |
| SC-4 | 运行目录自描述：清单指名每件产物及其格式，且解读该次运行不依赖别的运行目录 | 运行目录清单 + 清单内容 |
| SC-5 | 每个在范围内的方法都有明确的证据状态（已算 / 实测零覆盖 / 缺覆盖 / 歧义 / 排除），无静默丢行 | 各状态计数与函数明细总数对账一致 |
| SC-6 | CRAP 值可回溯到同快照的复杂度/覆盖率输入 | 抽样手算校验：低、中、高、零覆盖各至少一例，与产出值一致 |
| SC-7 | 类 / 文件 / 项目 / 全域四层聚合与函数明细对账一致 | 聚合表的计数与分位数，及其与明细的对账结果 |
| SC-8 | 基线运行是在真实测试执行下取得的，测试结果被如实记录 | 该次运行的测试调用日志、通过/跳过/失败数与退出码；若非全绿则报告显式标注偏向 |
| SC-9 | 报告先讲证据有效性，再讲分布与风险集中，再给排序后的发现与改进选项，局限醒目 | 报告全文 |
| SC-10 | 每条被排序的发现都指名具体方法、给出复杂度/覆盖率/CRAP/分位、并区分实测事实与推断 | 报告中的发现条目 |
| SC-11 | 判据基线未变差 | 交付后重跑判据：≥2392 通过 / 0 失败 / 退出码 0（跳过数与偶发失败按 Current Situation 的判别规则处理） |
| SC-12 | 维护者能凭仓库内文档独立复跑并解读 | `Document/` 下的说明文档 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 所有者原话：「看看在新的分支创建一下可持续测量的crap系统吧。最后产出在git仓库不追踪的路径。按日期时间命名。用sdd在新的worktree完成任务」；并在澄清中定了目标仓库 `DNF-JP-86-Server` 与「只做 C# Server」 | 在 `DNF-JP-86-Server` 内建任务分支与 worktree、新增采集系统所需的项目本地工程与配置（含为让 `.crap` 不被追踪而改 `.gitignore`）、执行构建与测试采集、写 `.crap/` 产物与 `Document/` 说明、写本任务的 SDD 工件，并做一次已评审结果的本地提交 |

## Requires Confirmation

- 合并回 `main`、push、建 PR、删分支或删 worktree，均需另行授权。
- 若采集需要联网安装本仓尚未使用的第三方工具，先说明再装。

## Assumptions

- 覆盖率与复杂度都能在 net10.0 / SDK 10.0.302 下取到方法级数据。若首选工具在
  .NET 10 上不可用，改选能满足同一测量契约的替代工具，并在清单里记明版本与理由；
  只有在**任何**可信的方法级覆盖率映射都取不到时才降级为复杂度诊断，且必须明说。
- `Dfo.Server.Utilities` 是空占位项目，会以零函数出现在范围内，不算数据缺口。

## Continuation Authorization

2026-09-08，所有者要求：「接手一下86 jp server下rks agent所创建的一个任务worktree。你来接手和继续crap那个任务」。复用原 worktree、基线与未提交任务成果，授权继续实现、评审、验证及一次本地成果提交。

revision 2 只同步当前用户要求的说明文档目录 Document/；目标与 SC-1..SC-12 不变。无旧缓存可用的验收可通过移走并保留缓存实现，不以删除缓存作为交付目的。