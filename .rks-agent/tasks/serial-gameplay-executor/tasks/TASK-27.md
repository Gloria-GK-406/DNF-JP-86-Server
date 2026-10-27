# TASK-27 — T-006e：F-115 修复（客户端提供、会扇出给他人的变长字段加上限）与 T-006 落地状态

基准提交 `8c531c9b`（TASK-26／T-006d 已提交，结果审查 approved）。范围取自：
- `verification/follow-ups.md` F-115（归属 TASK-27），以及与它相连的 F-113（每人上界含标题）、F-114（无生产写入方的字段）；
- `reviews/task-26-result-review.md` Round 1 发现 1，与 TASK-26「主控方处置（第 1 轮评审后）」第 1 条、「主控方处置（第 2 轮复核后，提交前）」第 3 条；
- `verification/t006-closure.md` §6 的 AC-013、RQ-036 两行，§8 的 TASK-27 一行，§9（M-2 进入条件），§11 队伍标题一行；
- `verification/measurement/selection-outbound-bound.md`「人群项（TASK-26）」一节；
- `spec.md` RQ-031(d)、RQ-036、RQ-082，AC-013，附录 G；`plan.md` 的 T-006 行与 M-2 行。

本单元的产出：一次同类字段排查、一处生产改动（`SET_PARTY_INFO` 解码器的标题长度上限）、配套的解码器与线级用例、协议记录同步，以及 T-006 收口的落地状态。

输入：
- TASK-26 的结果：人群项实测 `logs/task-26-crowd.txt`（§C 每人上界、§F 判定）、正式全量 `logs/task-26-build-test.txt`（8238 条）与评审后的 8239 条（`logs/task-26-review-fixes.txt`）、取证 `logs/task-26-violations.txt`（40 条）；
- TASK-26 的临时场景用例 `T26CrowdProbeTests.cs`（全文在 `logs/task-26-crowd.txt` 末尾，scratchpad `t26/` 有副本）：本单元线级用例的写法从它改写；
- 收口文件的写法与引用核对：`t006-closure.md`、scratchpad `t26/closure_refs.py`。

## Task Unit Contract

- Revision: 2（「主控方处置（执行中断后）」取代 W1 的线级复现写法，以及 Required check 2、停止条件 2 中与之相关的措辞）
- Outcome and references：
  1. **同类字段排查**（§2；F-115「同类」一句；TASK-26 处置第 1 条）：对全部已注册的 C→S 解码器做一次普查，找出「由客户端提供、长度可变、会被扇出给**其他**连接（或累积进其他连接一次 CMD 的发件箱）」的字段。逐个给出：入站解码器位置与当前长度约束、出站编码路径、扇出范围、最坏单帧字节、最坏单 CMD 发件箱字节、处置。普查结果落盘 `logs/task-27-census.txt`，结论表写进 `t006-closure.md` 落地状态一节。
  2. **队伍标题上限**（§3；F-115；RQ-036、AC-013；待裁定第 1、2 条）：`SetPartyInfoDecoder` 对 `titleLength > MaximumTitleBytes` 按该解码器既有的拒绝方式抛 `InvalidDataException`；不截断后照发。上限取值、依据等级与拒绝方式按待裁定第 1、2 条的裁定。排查若发现别的字段也要加上限，按停止条件 1 处理。
  3. **证据**（§4）：
     - 解码器边界用例：上限内通过、上限外拒绝；
     - 编码器向量：带满长标题的队伍名单帧为 80 ＋ 上限字节，钉住收口里用到的数；
     - 线级回归用例：超长标题被拒绝、同区新到的玩家连接不被关闭（先在基准上 RED，修复后 GREEN）；上限内的满长标题照常扇出，接收者连接保持。
  4. **通用防线评估**（§5；待裁定第 4、5 条）：只评估、给建议，不改 T-003 的发送端契约、不改 `OutboundLimits`。
  5. **协议记录同步**（§6；AGENTS.md「协议结论变更必须同步 `protocol/` 记录与 catalog 状态」）：`protocol/game/client-to-server/0x000C.md` 人工区、`protocol/catalog.md` 0x000C 行、`protocol/game/server-to-client/NOTI-0x0009.md`「待验证」。
  6. **收口**（§7、§8）：
     - `verification/t006-closure.md` 末尾追加「## 12. 落地状态（TASK-27）」：F-115 关闭；AC-013 与 RQ-036 两行的现状态；T-006 完成与否；M-2 进入条件；F-113 的每人上界改为 670 ＋ 标题上限并重算 N_max；Spec 修订（待裁定第 3 条接受时）；
     - `measurement/selection-outbound-bound.md`「人群项（TASK-26）」节末追加一小节落地状态，原文不动；
     - `follow-ups.md`：关闭 F-115，更新 F-113、F-114、F-69，文首补 TASK-27 一行，新增从 F-116 起编号。
  7. **一次正式全量**：带取证汇，反例类与对照以外为 0，`exe:` 为 0；表征测试 10 通过、0 跳过，`Characterization/` 与金样本目录不改；变异核对（§9）。
- Owned boundary：
  - **生产**：`Server/src/Dfo.Server.Protocol/Game/Party/PartyCommandSlice.cs`，只改 `SetPartyInfoDecoder`：一个常量、一处检查，以及一句说明上限出处的注释（AGENTS.md「注释只说代码说不出的」）。
  - **测试**：
    - `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyCommandCodecTests.cs`：加解码器边界用例；
    - `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyNotificationTests.cs`：加一条编码器向量；
    - `Server/tests/Dfo.Server.Networking.Tests/Integration/` 下新建线级用例文件一个（名字由执行者定，例如 `PartyTitleFanOutWireTests.cs`），只读使用 `Characterization/` 下的 `CharacterizationHost`、`CharacterizationSteps`、`WireClient`，不改它们。
  - **协议记录**：`protocol/game/client-to-server/0x000C.md`（人工区追加一节，机器区不动）、`protocol/catalog.md`（0x000C 一行的状态栏）、`protocol/game/server-to-client/NOTI-0x0009.md`（「待验证」加一条）。
  - **Spec**：待裁定第 3 条接受时，`spec.md` 附录 G 加 VC-9 一行；不接受时不改。
  - **文档与证据**：`verification/t006-closure.md`（只在末尾追加，前文不动）；`verification/measurement/selection-outbound-bound.md`（只在「人群项（TASK-26）」节末追加）；`verification/follow-ups.md`；`verification/logs/task-27-*.txt`；一次性脚本放 scratchpad `t27/`，全文附在对应日志末尾。
  - **不改**：
    - `Server/src` 中除上面那一个解码器以外的一切，包括 `OutboundLimits.cs`、`OutboundSender.cs`、发件箱与登记点；
    - `plan.md`、`AGENTS.md`、`Docs/`；`Characterization/` 目录与金样本文件；
    - `t006-test-dispositions.md`（本单元的测试增量写进落地状态一节的加法与本 Task 的 Result）。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。RED 阶段一次（只加了用例），修复之后一次，变异全部做完之后一次。
  2. **RED／GREEN**（`logs/task-27-red-green.txt`）：
     - 在不改生产代码时跑新用例：§4.1 的拒绝用例与 §4.3 的 W1 变红，W1 的失败要能看出是受害者连接被关闭（`OverLimit`），而不是别的原因；其余新用例此时为绿，逐条写明；
     - 修复之后全部变绿。两段都记命令、退出码与关键输出。
  3. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-27-build-test.txt`。
     - **总数**：基准 8239（TASK-26 评审后工作区：8238 ＋ 1）。本单元为 8239 ＋ N，逐项目写加法，N 逐条列名（预期见 §10）。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）与 AGENTS.md 已登记的偶发项（三样一起取身份；打包进程用例在满载下的就绪超时，按 F-55／F-103 的形状取身份并隔离复跑）；挂起转储 0。
     - **跳过**：18，不变。
     - **表征测试**：10 通过、0 跳过；`Characterization/` 与金样本目录无改动（`git status` 为证）。
     - **日志信号**：与 TASK-26 Checks 2 同口径、同脚本（scratchpad `t19/analyze.py`）。应为 0 的七项中，`GameplaySelfWait` 仍为 1（TASK-26 新加那一行用例的显示名），其余六项为 0；其余各项与 TASK-26 逐条对照。新线级用例会多出解码器抛异常的日志行（`Game decoder SetPartyInfoDecoder threw`）与连接关闭行，逐条归因到 W1。
     - 耗时与内存只作信息（RQ-091 用户修订：单条 ≤ 2 s 即可接受，性能不作停止条件）。F-69：用 scratchpad `t24/elapsed.py` 与 TASK-26 的正式全量（scratchpad `t26/formal-raw.txt`）逐条对比，多出 ≥ 9 秒的逐条隔离复核。
  4. **零违规取证**（`logs/task-27-violations.txt`）：`violations.py` 的命令与 TASK-26 Checks 3 相同。预期先写进日志开头再跑：40 条，`bus-leaked-flow` 2 ＋ `network-wait` 1 ＋ `write-off-executor` 37，与 TASK-26 相同；反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。
  5. **普查**（`logs/task-27-census.txt`，§2）：普查脚本全文；已注册 C→S 解码器的总数与逐条分类；分类的完备性加法（各类相加等于总数）；扇出字段表；协议清单中文本类协议「未注册」的核对；每条结论的文件:行。
  6. **变异核对**（`logs/task-27-mutations.txt`；程序同 TASK-26 Checks 6：每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 与 `-- Server/tests` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：§9 每一项都有指定的用例变红。
  7. **新增测试 ×10**（`logs/task-27-new-x10.txt`）：本单元新增的方法与行隔离连续 10 次，全部通过；`PartyCommandCodecTests`、`PartyNotificationTests` 全类 ×3。耗时只作信息。
  8. **检索集**（`logs/task-27-retrievals.txt`，§8.3）：每个检索式先写预期再跑。
  9. **收口引用核对**（`logs/task-27-closure-refs.txt`）：scratchpad `t26/closure_refs.py` 复制为 `t27/closure_refs.py`，范围扩到落地状态一节：引用的用例名与类名都能在 `Server/tests` 找到；引用的 `logs/…` 都存在；引用的 F 编号都在 `follow-ups.md`；落地状态报告「本单元关闭」的 F 编号在 `follow-ups.md` 中已关闭；移交的 F 编号未关闭；归属栏写了动作的行状态不是「信息」。全部 0 处不符。
  10. **测试改动核对**（`logs/task-27-diff-check.txt`）：`diffcheck7.py 8c531c9b --lines`（scratchpad `t07/diffcheck7.py`）加 `t21/methods.py`：方法集合只多了 N 中列出的、没有少的；断言行多重集合有变化的已有文件只有 Owned boundary 中点名的两个测试文件，且只有新增，逐处列名。
  11. **边界**（`logs/task-27-boundary.txt`）：`git diff --stat 8c531c9b -- Server/src` 只有 `PartyCommandSlice.cs`，且差异只落在 `SetPartyInfoDecoder` 内（列出差异块的行号范围）；`Server/src` 下没有未跟踪文件；`plan.md`、`AGENTS.md`、`Docs/` 无改动；`spec.md` 无改动或只有 VC-9 一行（待裁定第 3 条）；`Server/tests` 的改动只含 Owned boundary 列出的文件。
  12. **隔离复跑**（`logs/task-27-isolated.txt`）：Protocol、Networking 各一次，都带取证目录，记录各自的反例类与对照记录数。
  13. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。改动的已有文件行尾与基准一致（规划时核对：`PartyCommandSlice.cs`、`PartyCommandCodecTests.cs`、`PartyNotificationTests.cs`、`0x000C.md`、`NOTI-0x0009.md`、`catalog.md`、`spec.md`、`t006-closure.md`、`follow-ups.md`、`selection-outbound-bound.md` 都是 0 个 CR）；新建文件 LF。结束时执行 `dotnet build-server shutdown`；`pgrep -x yes` 为 0。
- Dependencies：
  - TASK-26（`8c531c9b`）：`t006-closure.md`、F-113～F-115、人群项实测与公式（`logs/task-26-crowd.txt`）、临时场景用例 `T26CrowdProbeTests.cs`、`HostOutputWiringSourceTests`（Host 195 条）、取证基线 40 条。
  - TASK-23～TASK-25：发送端 `OverLimit` 关连接（RQ-036）、RQ-031(d) 按收件人隔离、`OutboxWireTests`。
  - 脚本：scratchpad `t07/diffcheck7.py`、`t19/analyze.py`、`t21/methods.py`、`t24/elapsed.py`、`t25/mutate.py`、`t25/violations.sh`、`t26/closure_refs.py`、`t26/formal-raw.txt`，规划时核对都在；执行开始时再核对，缺失时从 TASK-25、TASK-26 日志末尾附的全文恢复。规划时的普查草稿脚本在 scratchpad `t27plan/census.py`，执行时复制为 `t27/census.py` 并按 §2.1 扩充。
- Result review：required。
  - 这是 T-006 内唯一一处玩法协议的生产改动：上限取低了，合法玩家改标题时会被断线；取高了或检查放错位置，F-115 仍在。解码器用例只能证明「检查按写下的数生效」，证明不了「这个数对客户端合法输入足够」；后者要靠审查核对依据与依据等级。
  - 普查的结论是判断性的：「某个字段不扇出」「某个字段已有上限」说错了，全量测试不会变红。Required check 5 只能证明表是完备的，证明不了每一行的判读对，要靠审查抽查文件:行。
  - 落地状态一节决定 T-006 是否完成、M-2 是否进入，以及 AC-013、RQ-036 的记法；写宽了（把仍有条件的记成无条件满足）不会被任何测试揭穿。
  - 若待裁定第 3 条接受，Spec 附录 G 多一行，交外部审核时要能对上。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 普查发现队伍标题以外，还有客户端提供、没有上限、会扇出给其他连接（或累积进其他连接一次 CMD）的字段。回报时附该字段的六项（§2.3 表头）与建议上限、依据等级。
    2. W1 在基准（不改生产代码）上不能变红，或变红的原因不是受害者连接被 `OverLimit` 关闭（说明 F-115 的情景与读码不符）。
    3. 按裁定的上限，任何现有用例、表征测试或金样本里的标题会被拒绝。
    4. 需要改 `OutboundLimits`、发送端、发件箱或登记点；需要改 `spec.md` 中 VC-9 以外的文字；需要改 `plan.md`、`AGENTS.md`。
    5. 正式全量取证出现 `exe:` 记录，或反例类与对照以外的记录；正式全量出现基线与已登记偶发项以外的失败。
    6. 裁定的拒绝方式在解码器内做不到（例如裁定为「保留连接、只丢该帧」，而传输层对解码器返回 null 的处理与之不符）。
  - 变异期间允许临时改动 `Server/src` 与测试，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 范围、与 F-115 原文的差异

**与 F-115 一致的部分**：只有队伍标题需要加上限（§2 的规划时普查结论，执行时复核）；超长请求按解码器既有方式拒绝，不截断；修复后 F-113 的每人上界改为 670 ＋ 上限并重算。

**规划时发现、F-115 与 TASK-26 没有写到的**：
1. **标题会到达的连接比 F-115 写的多一类**。除了队员与区域内新到的人，标题还在两条路上出站：创建或修改队伍时，区域内已在的观察者各收一帧队伍名单（`protocol/patterns/data/traces/a21-party-directory-live-20260913.md`：observer 收到 61 字节 type-0 refresh）；队员收 type-1 设置帧，长 37 ＋ T（body 5 ＋ (16 ＋ T) ＋ 1，`PartyNotificationSlice.cs` 的 `PartySettingsEncoder`）。这两条每个收件人每条 CMD 只有一帧，不累积；会累积的只有新到者那一条（每个本人不在的队伍一帧，`RefreshObservedPartiesAsync`，调用方 5 处：`TownAreaProjectionDelivery.cs:198`、`:507`、`:518`、`:553`、`:576`）。所以 F-113 的「每人 670 ＋ T」仍是最坏累积的正确形状。
2. **宠物文本有一个生产写入方**。TASK-26 的人群项读码（`logs/task-26-crowd.txt` §C、F-114）说宠物文本「生产写入方都写空」，但宠物改名（`RENAME_CREATURE`）会写入。改名经规则限制在 12 字节以内（`SpecialConsumableEffectPlanRuleService.cs:110-117`、`RenamePetEffectHandler`，`SpecialConsumableEffectHandlers.cs:72`），不超过「无宠物」文本的 12 字节，所以每人 539 B 的上界不受影响。执行时核实改名写入的就是 0x0002 名牌读取的 `CreatureText`（`StateSynchronizationPayloadCodec.Entry.cs:936`），核实后在 F-114 与 `selection-outbound-bound.md` 的落地状态里更正这句话；核实结果与此不符时（改名写的不是名牌字段），也照实写，不影响结论。
3. **没有客户端上限证据**。`protocol/` 中队伍标题只有下界证据：抓包里 1 字节的 `A`、`B`（`0x000C.md`「同区公共目录 inline-title」一节，ClientVerified），以及 2026-10-18 运行日志里 28 字节的请求体，即 12 字节标题（`Document/PartyCycleEntryFix-20261018.md:7`，只有长度、没有内容）。客户端编辑框的字数限制与 S→C 读取 raw-dstr 的 `maxLen`（`a21-party-settings-static-20260912.md`：读取分支在 VA `0x01172152..0x01172174`）都没有记载。本机 `dnf-client/DNF.exe` 的 SHA-256 为 `f78f0aea…a7fc04c`，不是 `protocol/` 记载的 A21 客户端（`1F8244D3…`、`CA7ECA4E…`），也没有内存转储，所以本单元做不了 ClientStatic 取证。上限取值因此交待裁定第 1 条。

**「不超过客户端可能合法产生的长度」的读法**：本计划读作「上限不低于客户端可能合法产生的最大长度」，也就是合法请求永远不会被拒绝；没有客户端证据时，取值宁高勿低，并写明依据等级。若主控方的本意是「上限等于客户端的实际上限」，那只能等 ClientStatic 取证（待裁定第 1 条备选 B）。

**本单元不做的**：发送端或发件箱的通用防线（只评估，§5）；F-113 的人数上限（交主控方立项的状态不变）；F-114 的导入与 GM 写库约束；M-2 本身；T-007～T-009 的任何事项。

### 1. 基准核实（执行时重跑并落盘 `logs/task-27-verify.txt`）

| # | 问题 | 检索 | 规划时在 `8c531c9b` 上的结果 |
|---|---|---|---|
| V1 | 工作区与基准 | `git status --short`；`git log -1` | 干净；HEAD `8c531c9b` |
| V2 | 测试总数与取证基线 | 读 `logs/task-26-build-test.txt`、`logs/task-26-review-fixes.txt`、`logs/task-26-violations.txt` | 正式全量 8238；评审后新增 1 条（Host），工作区 8239 ＝ HttpApi 54 ＋ Pvf 80 ＋ Protocol 1746 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 179 ＋ Application 4538 ＋ Host 195；失败 2（基线）、跳过 18；取证 40 ＝ 2 ＋ 1 ＋ 37 |
| V3 | 标题解码与拒绝方式 | 读 `PartyCommandSlice.cs:208-266`；读 `NetworkPlugins.cs` 的 Game `Decode` | 标题长度 `i32`，只拒绝负数，再要求 body 恰为 16 ＋ T；严格 UTF-8。解码器抛异常时，传输层记 `Game decoder … threw` 后重新抛出，连接被关闭（`NetworkPlugins.cs` Game `Decode` 的 remarks 与 catch）；返回 null 记为「服务端缺陷」（declined）并丢帧 |
| V4 | 标题的唯一产出点 | `grep -rn "new PartyTitle(" Server/src` | 1 处，`PartyCommandSlice.cs:258`；其余全用 `PartyTitle.Empty` |
| V5 | 标题的出站 | `grep -rn "PartyInformationBlockEncoder.Encode" Server/src`；读 `PartyRosterEncoder`、`PartySettingsEncoder` | 名单帧 15 ＋ 5 ＋ (16 ＋ T) ＋ 44 ＝ 80 ＋ T；设置帧 15 ＋ 5 ＋ (16 ＋ T) ＋ 1 ＝ 37 ＋ T；换队长帧不带信息块 |
| V6 | 帧长上限 | 读 `Networking/Framing/DnfFrameProtocol.cs:12`、`OutboundEncoder.cs:68` | 入站、出站都是 1,048,576 B（含头）。入站 body ≤ 1,048,562，T ≤ 1,048,546；出站名单帧 80 ＋ T ≤ 1,048,576，T ≤ 1,048,496 才编码得出 |
| V7 | 发送端超限 | 读 `OutboundSender.cs` `ConnectionQueue.Enqueue` | `bytes + item.Length > QueueByteLimit` 即 `OverLimit` 并关连接，不区分 Required／BestEffort |
| V8 | 观察者刷新的调用方 | `grep -rn "RefreshObservedPartiesAsync" Server/src/Dfo.Server.Protocol` | 定义 1 处，调用 5 处（§0 第 1 条） |
| V9 | 现有用例里的标题长度 | `grep -rnE "PartyTitle\(|SetPartyInfo|0x000C" Server/tests` 后逐个读标题字节 | 最长 12 字节（`E697A0E9989FE4BC8DE5908D`，`PartyCommandCodecTests`、`PartyNotificationTests`、`HandlerFailureConnectionIntegrationTests`）；表征测试的组队步骤不设标题 |
| V10 | 宠物名的上限与写入字段 | 读 `CreatureMessages.cs:145-161`、`SpecialConsumableEffectPlanRuleService.cs:110-120`、`SpecialConsumableEffectHandlers.cs:68-85`、改名实例的 `RenameAsync` | 解码只要求长度自洽；规则与效果处理器都限 ≤ 12 B、无控制字符；写入字段执行时核实（§0 第 2 条） |
| V11 | 脚本 | `ls` scratchpad 下 Dependencies 列出的脚本 | 都在 |

V1～V11 与本表不一致时：V2 的总数不同，停下回报（说明 TASK-26 之后有未记录的改动）；V4 多出产出点，把它纳入 §3 的检查范围并记录；V9 出现超过裁定上限的标题，按停止条件 3 处理；其余不一致记录后照常进行。

### 2. 同类字段排查（Outcome 1）

**2.1 方法**（脚本 scratchpad `t27/census.py`，以规划时的 `t27plan/census.py` 为起点）：
1. 从 `ProtocolModule.cs` 抽出全部 C→S 注册（`AddGameCmdMessageDecoder<…>`、`AddGameNotiMessageDecoder<…>`、`AddChannelMessageDecoder<…>`，含 `RegisterInboundDecoders` 里用 `GameProtocol.*Type` 常量的写法），得到「线级键 → 解码器类」表与总数。
2. 对每个解码器类（含它委托的 `*PayloadCodec.Decode` 等），按 body 长度规则分三类：**定长**（等号或 `RequireLength`）、**有界变长**（读出的长度或计数与常量比较）、**开放**（只有下限，或长度只要求自洽）。各类相加等于第 1 步的总数，加法写进日志。
3. 对「有界变长」与「开放」两类，逐个追到它保留下来的字节去了哪里：丢弃、只记日志、只回本人、写入状态后被别人读到、直接扇出。只有后两种进入 §2.3 的表。
4. 对照 `protocol/patterns/data/protocol-inventory.tsv` 中名字像文本或社交的 C→S 协议（聊天、私聊、喊话、摆摊、公会、交易、好友、招募、留言、备忘），逐个核对是否已注册解码器。未注册的帧由传输层按「Unhandled Game frame」丢弃，不扇出。
5. 不在范围内：GM 与 HTTP 的输入（服务器公告、官方邮件）。它们不是客户端提供的，入口有管理员访问边界。

**2.2 规划时的普查草稿**（`t27plan/census.py` 对 `Server/src/Dfo.Server.Protocol` 的粗筛：160 个解码器类；执行时按 2.1 补齐注册表一侧并逐个确认）。

**2.3 规划时的结论表**（执行时逐行复核文件:行，并把字节数按编码器重算；「最坏单帧」与「最坏单 CMD」都含 15 B 帧头，指落在**某一个其他连接**上的字节）：

| 字段（协议） | 入站解码器与当前约束 | 出站与扇出范围 | 最坏单帧 | 最坏单 CMD（单个其他连接） | 处置 |
|---|---|---|---:|---:|---|
| 队伍标题（`SET_PARTY_INFO` 0x000C） | `PartyCommandSlice.cs:234-243`：`i32`，只拒负数，body 恰为 16 ＋ T；入站帧限 T ≤ 1,048,546 | 名单帧给区域观察者与新到者，设置帧给队员（V5、§0 第 1 条） | 今天 80 ＋ T，可编码时最大 1,048,576；修复后 80 ＋ C | 今天无上界（一帧即可占满 1 MiB 队列）；修复后新到者 52 ＋ N × (670 ＋ C) | **加上限 C**（§3，待裁定第 1、2 条） |
| 宠物名（`RENAME_CREATURE` 0x0064） | `CreatureMessages.cs:154-158`：长度只要求自洽；规则 ≤ 12 B（`SpecialConsumableEffectPlanRuleService.cs:117`），通不过不写入、不扇出 | `CreatureRenameNotification` 经 `CreatureProjectionDelivery.SendVisibleAsync` 给同区、同局或同塔的人；名牌进 0x0002 在场快照 | 15 ＋ 6 ＋ 12 ＝ 33 | 33（改名）；在场快照内 ≤ 12，已含在 539 之内 | 已有上限，不改（解码器放宽不影响扇出） |
| 角色名（`CREATE_CHARACTER` 0x0005） | `CharacterNameField.cs:22-47`：`i32` ＞ 0 且不超过 body；规则创建 ≤ 18 B（`CharacterCreationRuleService.cs:22`）；`CHECK_DOUBLE_CHARACTER_NAME` 0x02B5 ≤ 30 B 只回本人 | 在场快照、队伍邀请等，给同区与相关的人 | 已含在 0x0002 子类型 0 的 539 之内 | 已含在每人 670 之内 | 已有上限，不改 |
| 婚礼留言（`UPDATE_GUEST_COMMENT` 0x03F8） | `MarriageGuestbookSlice.cs:20-21`：≤ 20 B；规则 ≤ 20 B、每本 ≤ 100 条（`Guestbook.cs:9`、`:15`、`:21`） | 别人用 `LOAD_GUEST_COMMENT` 0x03FA 读出，累积进读者自己的一条 CMD | — | 15 ＋ 13 ＋ 100 × (4 ＋ 30 ＋ 4 ＋ 4 ＋ 20) ＝ 15 ＋ 13 ＋ 6,200 ＝ 6,228 | 已有上限，不改 |
| 邮件正文与收件人名（`MAILBOX_SEND` 0x005E、`MULTI_MAILBOX_SEND` 0x013B） | `MailboxSendWire.cs:19`：请求 ≤ 1,024 B；正文 ≤ 512 B（`:43`）；收件人名 ≤ 19 B（`:23`）；附件 ≤ 10 | 收件人在线时只收一帧提醒（`MailDeliveredHandler.cs:13`，计数）；正文在收件人打开邮箱时随一页（20 封，`MailboxQueryDomainService.cs:16`）回给收件人 | 提醒为定长小帧 | 一页 ≤ 20 封 ×（≤ 512 ＋ 每封固定部分与附件），执行时按列表编码器算出 | 已有上限，不改 |
| 婚礼礼金附言（`WEDDING_MONEY_GIFT` 0x03F2） | `MarriageWeddingSlice.cs:88-90`：≤ 1,024 B | 解码后丢弃：`WeddingGiftCommand` 不带文本（`:160`） | — | — | 不扇出 |
| 强化、锻造上报的物品名（`UPGRADE_ITEM`、`UPGRADE_ITEM_SEPARATE`） | `ReinforceItemMessageDecoder.cs:15`、`ForgingMessages.cs:31`：≤ 128 B | 只用于校验与日志 | — | — | 不扇出 |
| 生物脚本消息（0x007A） | `CreatureReports.cs:37-39`：≤ 256 B | 只记日志（`CreatureScriptMessageReportHandler`） | — | — | 不扇出 |
| UDP 端点上报的尾部（`SET_UDP_IP_PORT` 0x0002） | `PartyCommandSlice.cs:130`：固定 15 B 之后的尾部开放 | 尾部 `IdentityTail` 无人读（检索 1 处，即定义）；端点本身定长 | — | — | 不扇出 |
| 组队邀请与接受（`REQUEST_PEER` 0x000A、`RESPONSE_PEER` 0x000B） | `PartyCommandSlice.cs:138`、`:152`：7～9 B、7～16 B | 给被邀请者或邀请者 | 定长小帧 | 定长小帧 | 已有上限 |
| 客户端设置（0x00C5、0x00C6、0x0170、0x01C0 等） | `ClientSettingsCommands.cs:53-54`：≤ 65,536 B；快捷聊天恰为 300 B；角色选项 516 B | 只在本人登录或选角时回放给本人 | — | — | 只回本人，不在范围 |
| 遥测与上报（`UnansweredReportSlices.cs`、`SecurityStatusReport`、`ClientSpec*`、`SessionMetrics`、`ClientHardwareDetail`） | 多为开放 | 只记日志 | — | — | 不扇出 |
| 聊天、私聊、喊话、摆摊（0x0052～0x0056）、公会（0x0043 起、0x009A、0x00E0、0x0155 等）、交易（0x0018）、好友（0x0122） | 未注册解码器 | 按未注册丢弃 | — | — | 不扇出 |

结论：需要加上限的只有队伍标题。执行时任何一行复核不成立，按停止条件 1 处理。

**2.4 落盘**：`logs/task-27-census.txt` 写全表（含被排除的每个解码器及排除理由）；`t006-closure.md` 落地状态只放 §2.3 那张结论表与完备性加法，不抄过程。

### 3. 队伍标题上限（Outcome 2）

**3.1 改动**（`SetPartyInfoDecoder`，`PartyCommandSlice.cs:208-266`）：
- 加常量 `MaximumTitleBytes`（值按待裁定第 1 条；名字与可见性执行者定，`internal const` 即可，测试经 InternalsVisibleTo 引用）。
- 在「`titleLength < 0` 即拒绝」之后、`expectedLength` 比较之前，加一行：`titleLength > MaximumTitleBytes` 时抛 `InvalidDataException`，消息写明上限，例如 `SET_PARTY_INFO title length '{titleLength}' exceeds '{MaximumTitleBytes}' bytes.`。放在长度自洽检查之前，是为了让一个宣称超长、body 又被截断的请求也落在这条拒绝上，日志能说清原因。
- 注释一句：上限的出处与依据等级（指向 `protocol/game/client-to-server/0x000C.md` 本单元新加的那一节），以及「超长不截断，因为截断会改变队员与观察者看到的标题」。

**3.2 拒绝方式**（待裁定第 2 条，建议沿用抛异常）：同一解码器对负长度、非法 UTF-8、长度不自洽都是抛 `InvalidDataException`，传输层随后关闭发送者的连接（V3）。超长标题沿用这一方式。受影响的只有发送超长标题的那条连接，正是 F-115 里的攻击者；受害者因此收不到任何该队伍的帧。

**3.3 只在解码器上加检查的理由**：标题唯一的产出点就是这个解码器（V4）；`IPartyService.CreateOrUpdateSettings` 的其他调用方都传 `PartyTitle.Empty`。所以不需要在领域服务或 `PartyTitle` 模型上再加一道，也不改 Abstractions 契约。检索 R1 钉住「产出点只有 1 处」。

### 4. 证据（Outcome 3）

**4.1 解码器边界用例**（`PartyCommandCodecTests.cs`，仿该文件既有的 `Set_party_info_rejects_negative_overflowing_truncated_and_invalid_utf8_titles` 写法）：
- P1 `Set_party_info_accepts_a_title_of_exactly_the_maximum_length`（Theory，2 行）：ASCII 填满 C 字节；多字节 UTF-8 填满 C 字节（例如若干个 3 字节字符，余数用 ASCII 补齐）。断言解码成功、`Title.ToBytes()` 与输入相同。
- P2 `Set_party_info_rejects_a_title_longer_than_the_maximum`（Theory，3 行）：C ＋ 1 字节且 body 自洽；1,048,400 字节且 body 自洽（F-115 的情景）；宣称 C ＋ 1、body 被截断。都抛 `InvalidDataException`。
- 构造 body 的辅助方法写在测试类内，按 `2 前导字节 ＋ i32 长度 ＋ 标题 ＋ 10 尾字节` 拼，尾字节取该文件已有的合法 Normal 设置。

**4.2 编码器向量**（`PartyNotificationTests.cs`）：
- P3 `Full_roster_with_a_maximum_length_title_is_eighty_bytes_plus_the_title`：快照带 C 字节标题，`PartyRosterEncoder` 编出的帧长 80 ＋ C（body 65 ＋ C），标题字节位于信息块的 raw-dstr 中。上限用常量引用，所以这条钉的是「80 ＋ 上限」这个公式，不是某个具体数。

**4.3 线级回归用例**（新文件，`Networking.Tests/Integration/`，`[PvfFact]`，`[Collection(CharacterizationCollection.Name)]`，写法从 `T26CrowdProbeTests` 改写）：
- W1 `A_party_title_above_the_limit_is_refused_and_a_player_walking_in_keeps_its_connection`：
  1. 两名玩家进世界；攻击者走进共享区域，等对方 `FenceAsync` 收齐；
  2. 攻击者发 `0x000C`，无队创建形状，`titleIndex = 0`，标题 1,048,400 字节（帧长 14 ＋ 16 ＋ 1,048,400 ＝ 1,048,430 ≤ 1 MiB，能被接收；修复前服务端编出的名单帧 1,048,480 ≤ 1 MiB，能被编码）；
  3. 断言攻击者的连接被服务端关闭（`ReadUntilClosedAsync`，拒绝方式按待裁定第 2 条；裁定为别的方式时改这一步的断言）；
  4. 受害者走进同一区域，`FenceAsync` 成功（连接仍开着），且收到的帧里没有攻击者队伍的名单帧。
  - 修复前（RED）：第 2 步被接受；第 4 步受害者的这条 CMD 先登记约 320 B，再登记 1,048,480 B 的名单帧，超过 1,048,576 B，被发送端按 `OverLimit` 关闭，`FenceAsync` 失败。Required check 2 要从日志里认出这条 `OverLimit`。
- W2 `A_party_title_at_the_limit_reaches_a_player_walking_in`：攻击者换成普通队长，标题恰为 C 字节；受害者走进区域后收到一帧 80 ＋ C 字节的名单帧，内含该标题，连接保持。它证明上限内的扇出照常进行，也在线上钉住 80 ＋ C。
- 两条都不改 `Characterization/` 下的任何文件，也不进金样本。

### 5. 通用防线评估（Outcome 4）

规划时的判断（执行时不需要再做实验，只把结论写进落地状态一节，并新增 F-116）：
- **加了标题上限之后，客户端能控制的单帧扇出最大是 80 ＋ C**（§2.3），不再有「一帧就占满受害者队列」的路径。剩下能让受害者因队列超限断线的，是人数（F-113，按主控方裁定已接受为适用范围）与导入或写库放大的字段（F-114），都不是一条客户端请求能做到的。
- **攻击者连续改标题**：每次修改给区域内每个观察者一帧，每帧 ≤ 80 ＋ C。入站每条连接至多 1 条待执行 CMD（AC-003），攻击者的速率受自己的入站串行约束；受害者要落后 ⌈1,048,576 ÷ (80 ＋ C)⌉ 次修改才会超限（C ＝ 256 时 3,121 次）。修复前是 1 次。与基准的 2 s 直发期限相比，这是同量级的慢客户端行为（VC-8），不是 T-006 新增的第三方断线。
- **可选的通用防线**，都会改已定契约，交待裁定第 4 条：
  - (a) 发送端：BestEffort 条目会超限时只丢该条、不关连接。与 RQ-036「该连接的发送队列超过上限时，关闭这个连接」的字面相反，要改 Spec 与 T-003 的发送端契约；丢掉的若是在场或队伍状态帧，客户端会静默失步，比断线更难察觉。
  - (b) 登记点：发往非来源连接的单帧大于某阈值时，按 RQ-031(d) 只丢该收件人的条目并记日志。改的是 RQ-031(d) 的触发集合（今天只有编码失败），也是共享契约。
  - (c) 约定：AGENTS.md「快速开发：新增一个协议的固定动作」加一句「客户端提供、会扇出给他人的变长字段，解码器必须有上限，并在协议记录里写明依据」。不改代码，靠评审守（待裁定第 5 条）。

### 6. 协议记录同步（Outcome 5）

- `protocol/game/client-to-server/0x000C.md` 人工区末尾追加一节「YYYY-MM-DD 标题长度上限（CurrentImplementation）」：
  - 服务端拒绝 `titleLength > C` 的 inline-title 请求，方式与负长度、非法 UTF-8 相同（解码失败，发送者连接关闭）；不截断；
  - 依据等级：客户端最大长度**没有证据**；已知下界为 1 B（抓包，ClientVerified）与 12 B（运行日志中 28 字节的请求体，只有长度）；C 是服务端策略，按待裁定第 1 条裁定的理由写一句；
  - 这一节只写结论与指向代码的链接（`evidence.md`「什么不属于证据库」：服务端设计不在协议文档里展开）。
- `protocol/catalog.md` 0x000C 一行的状态栏末尾补「；inline-title 长度上限 C B 为 CurrentImplementation，客户端上限待 ClientStatic」。
- `protocol/game/server-to-client/NOTI-0x0009.md`「待验证」加一条：「客户端读取 type 0／1 信息块 raw-dstr 的 `maxLen`（VA `0x01172152..0x01172174`）与 `0x000C` 编辑框的字数限制；服务端发出的标题不超过 `0x000C` 的上限 C」。
- 这三处都是追加，不改既有结论；没有结论被推翻，所以不需要「为什么被推翻」一段。

### 7. `t006-closure.md` 落地状态（Outcome 6）

在文件末尾追加「## 12. 落地状态（TASK-27，日期，基准 `8c531c9b`）」，前文一字不动。内容：
1. **F-115 关闭**：改动位置（文件:行）、上限 C 与依据等级、拒绝方式；证据用例 P1～P3、W1、W2 的名字；RED／GREEN 与变异的日志。
2. **同类字段排查**：§2.3 的结论表与完备性加法；结论「只有队伍标题需要加上限」。
3. **AC-013、RQ-036 的现状态**：仍是「满足，附条件」，条件只剩两条——同区他人或 PvP 大厅人数（F-113，适用范围按下面重算的数）、无生产写入方的字段（F-114）；§6 两行与 §9 中「保守上界对恶意输入不成立，由 TASK-27 修复」一句自本节起作废，原文保留。
4. **F-113 重算**（按 C 填；下面是 C ＝ 256 时的数，供审查核对公式）：
   - `inbound:GameSetUserArea`：每人 670 ＋ 256 ＝ 926；N_max ＝ ⌊(524,288 － 52) ÷ 926⌋ ＝ ⌊524,236 ÷ 926⌋ ＝ 566（566 × 926 ＝ 524,116 ≤ 524,236；567 × 926 ＝ 525,042 ＞ 524,236）；超过 1,048,576 B 的人数：1,048,524 ÷ 926，1,132 × 926 ＝ 1,048,232 ≤ 1,048,524，1,133 × 926 ＝ 1,049,158 ＞ 1,048,524，所以是 1,133；20 人规模（他人 19）：52 ＋ 19 × 926 ＝ 52 ＋ 17,594 ＝ 17,646 B，还能多 566 － 19 ＝ 547 人。
   - 选角（PvP 频道）：不含队伍帧，133／1,140 不变。
   - 其余城镇进入：⌊(524,288 － F) ÷ 926⌋。
5. **T-006 完成与否**：T-006 的五个单元（TASK-23～TASK-27）都有证据；TASK-27 经结果审查并提交后，T-006 完成。
6. **M-2 进入条件**：TASK-26 与 TASK-27 都经结果审查并提交；按 §9 的三条逐条写成立与否。
7. **§7 测试数量加法续写**：8238（TASK-26 正式全量）→ 8239（TASK-26 评审后）→ 8239 ＋ N（TASK-27），N 逐条列名。
8. **§8 移交清单的变化**：TASK-27 一行关闭；新增的 F 编号与归属。
9. **§10 Spec 修订**：待裁定第 3 条接受时补一行「附录 G VC-9（超长队伍标题被拒绝）｜文字已改，新增可见变化｜裁定条目｜本单元」。
10. **§11 的变化**：队伍标题一行由「不接受，由 TASK-27 修复」改为「已修复」（写在本节，不改 §11 原行）；新增「标题上限没有客户端证据」一条已接受的证明边界（F-117）。

`measurement/selection-outbound-bound.md`：「人群项（TASK-26）」节末追加「### 落地状态（TASK-27）」：每人上界表的队伍名单帧一行改记为 80 ＋ C（C 的值与出处），城镇换区每人 670 ＋ C，判定表 `inbound:GameSetUserArea` 一行按上面重算；宠物文本一句的更正（§0 第 2 条）。原文不动。

### 8. `follow-ups.md`、检索集

**8.1 `follow-ups.md`**：
- 关闭 F-115（「已在 TASK-27 关闭」：上限、拒绝方式、证据用例、`t006-closure.md` §12）；
- 更新 F-113：每人上界写成 670 ＋ C，N_max 与超限人数按 §7 第 4 条；
- 更新 F-114：宠物文本一句按 §0 第 2 条的核实结果更正；
- 更新 F-69（本单元与 TASK-26 的耗时对比）；
- 新增 F-116：通用防线的三个选项与评估（§5），归属按待裁定第 4、5 条的裁定（建议：T-009 或交主控方立项，不阻断 M-2）；
- 新增 F-117：队伍标题的客户端上限待 ClientStatic 取证（A21 客户端 S→C raw-dstr 读取的 `maxLen` 与 C→S 编辑框字数限制）；取证结果若大于 C，必须上调 C，否则合法玩家会被断线；若小于 C，可以收紧，也可以不动。归属：用户（需要 Windows 端的 A21 客户端或其内存转储）或客户端研究仓库；
- 执行中发现的其他事项从 F-118 起编号；
- 文首补一行 TASK-27 的摘要（仿 TASK-26 那一行）。

**8.2 写法**：F-117 是「信息」还是「未关闭」，看归属栏是否写了动作（TASK-26 Required check 9 的规则）。

**8.3 检索集**（Required check 8，先写预期再跑，每条写命令全文与输出行数）：

| # | 检索 | 预期 |
|---|---|---|
| R1 | `grep -rn "new PartyTitle(" Server/src` | 1 行，`PartyCommandSlice.cs` |
| R2 | `grep -rn "MaximumTitleBytes" Server/src Server/tests`（名字按实际） | 定义 1 行 ＋ 检查 1 行 ＋ 测试引用若干，逐行列出 |
| R3 | `grep -rn "RefreshObservedPartiesAsync" Server/src/Dfo.Server.Protocol` | 定义 1 ＋ 调用 5 |
| R4 | `grep -rn "PartyInformationBlockEncoder.Encode" Server/src` | 2 行（名单帧、设置帧），与 V5 一致 |
| R5 | 普查脚本的注册总数与分类加法 | 各类相加等于总数；扇出字段表与 §2.3 相同 |
| R6 | `git diff 8c531c9b -- Server/src` 的差异块 | 只在 `SetPartyInfoDecoder` 内 |

### 9. 变异（Required check 6）

| 变异 | 改动 | 应当变红 |
|---|---|---|
| MT1 | 删去上限检查（生产侧） | P2 前两行；W1。P2 第 3 行（宣称超长、body 截断）仍由长度自洽检查拒绝，预期保持绿，如实记录 |
| MT2 | `>` 改为 `>=`（生产侧，差一） | P1 两行；W2 |
| MT3 | 上限常量减半（生产侧） | P1 两行；W2。P3 引用常量，预期保持绿，说明它钉的是公式 |
| MT4 | 检查挪到长度自洽检查之后（生产侧） | P2 第 3 行的异常消息不再提上限：若 P2 断言了消息里有上限字样则变红；若 P2 只断言异常类型，则记「不变红」，并在 Result 里说明这条变异只影响日志可读性 |

人群项与选角的字节数不受本单元影响，不重测；W2 的 80 ＋ C 是新的线上实测点。

### 10. 数量与取证预期

- **N**（预计 8，执行时按实际运行例数写）：
  - Protocol：P1 2 行 ＋ P2 3 行 ＋ P3 1 条 ＝ 6；
  - Networking：W1、W2 ＝ 2。
  - 预期总数 8239 ＋ 8 ＝ 8247；逐项目只有 Protocol（1746 → 1752）与 Networking（179 → 181）变化。
- **D**：0。
- **取证**：40 条，与 TASK-26 相同。W1、W2 经执行器运行真实宿主，不在执行器外写状态；P1～P3 是纯编解码。`exe:` 0。

### 11. 执行顺序

1. §1 核实（V1～V11），落盘 `task-27-verify.txt`。
2. **§2 普查**：脚本、分类、完备性加法、扇出表。触发停止条件 1 即停下回报；此时工作区只有日志。
3. §4 先写用例（P1～P3、W1、W2），不改生产代码，构建并跑新用例：RED 段（Required check 2）。W1 不按预期变红即按停止条件 2 停下。
4. §3 改解码器，构建（`--no-incremental`），跑新用例：GREEN 段；Protocol、Networking 过滤运行。
5. 正式全量与取证（Required checks 1、3、4），隔离复跑（check 12），×10／×3（check 7）。
6. §9 变异，重建。
7. §6 协议记录；待裁定第 3 条接受时改 `spec.md` 附录 G；§7 落地状态与 `selection-outbound-bound.md`；§8 `follow-ups.md`；检索集。
8. 收口引用核对、diff 核对、边界、`cr-at-eol` 口径 `diff --check`（checks 9～11、13）。

### 12. 表征测试、金样本与生产行为

- 生产行为唯一的变化：inline-title 长度超过 C 的 `SET_PARTY_INFO` 被拒绝，发送者连接关闭（按待裁定第 2 条）。V9 说明现有用例与表征测试的标题都远小于 C。
- 表征测试 10 条与全部金样本不变；有一条需要改，按停止条件 3 处理。
- 这一变化是否要进附录 G，见待裁定第 3 条。

### 13. 范围来源的去向

| 来源 | 本单元 |
|---|---|
| F-115：队伍标题没有上限 | §3，Outcome 2 |
| F-115「同类」一句；TASK-26 处置第 1 条「同类问题由 TASK-27 规划时排查」 | §2，Outcome 1 |
| TASK-26 第 1 轮评审发现 1 的失败情景 | W1（§4.3） |
| TASK-26 第 2 轮复核后处置第 3 条：F-113 改为 670 ＋ 上限并重算 N_max | §7 第 4 条，§8.1 |
| TASK-26 处置第 1 条：TASK-27 完成后在 `t006-closure.md` 末尾追加落地状态 | §7 |
| `t006-closure.md` §9：M-2 进入条件 | §7 第 6 条 |
| F-114 中宠物文本的说法 | §0 第 2 条，§8.1 |
| AGENTS.md：协议结论变更同步 `protocol/` | §6 |
| RQ-082：可见变化只允许附录 G 所列 | 待裁定第 3 条 |
| RQ-036、RQ-031(d)：发送端与登记点的既定契约 | §5，待裁定第 4 条 |

## 待裁定

1. **标题上限 C 取多少**（§0 第 3 条、§3.1；F-115）。
   - 现有证据只有下界：1 B（抓包，ClientVerified）、12 B（运行日志，只有长度）。客户端编辑框字数与 S→C 读取的 `maxLen` 都没有记载；本机客户端不是 A21 版本，本单元取不到 ClientStatic。
   - **建议 A：C ＝ 256 B，依据等级记为「推断，无客户端上限证据」。** 理由：
     - 队伍标题是目录行里的一行短标签，与名字同类。本仓同类客户端文本的既有上限：宠物名 12 B、角色名 18 B（创建）／30 B（查重）、婚礼留言 20 B、邮件收件人 19 B；更长的类别：物品名 128 B、生物脚本消息 256 B、邮件正文 512 B。256 B 是名字类最大值 30 B 的 8 倍以上、已观察 12 B 的 21 倍，UTF-8 下可容 85 个三字节字符；
     - 取高是为了不误拒合法请求（误拒的后果是合法玩家改标题时被断线）；
     - 扇出代价可接受：每人 926 B，N_max 566，20 人规模 17,646 B（§7 第 4 条）。
     - 同时立项 F-117 取 ClientStatic，结果大于 256 时必须上调。
   - 备选 B：先取 ClientStatic 再定 C。需要用户在 Windows 端用 A21 客户端或内存转储，读出 VA `0x01172152..0x01172174` 处 raw-dstr 读取的 `maxLen`，以及 `0x009C649A..0x009C64AC` 构包前编辑框的字数限制。证据最好，但本单元要等它，T-006 与 M-2 随之推迟。
   - 备选 C：C ＝ 1,024 B（与婚礼礼金附言、邮件请求总长同值）。更不容易误拒；N_max 降到 309，20 人规模 32,238 B。

2. **超长时的拒绝方式**（§3.2）。
   - **建议：沿用该解码器既有方式，抛 `InvalidDataException`，传输层关闭发送者连接。** 理由：负长度、非法 UTF-8、长度不自洽已经这样处理；这个协议没有已知的拒绝回包（`0x000C.md`：「inline-title 更新、非队长拒绝与拒绝 response 仍待当前实帧」），没有别的可用拒绝形状；受影响的只有发送者。
   - 备选：解码器返回 null，丢帧、保留连接。代价：传输层把「已注册解码器返回 null」记为服务端缺陷（`NetworkPlugins.cs` Game `Decode` 的 remarks），语义不符；客户端也收不到任何回应，界面状态未知。选它时 W1 第 3 步改为断言连接仍开着、没有任何队伍帧。

3. **这一变化是否写进 Spec 附录 G**（RQ-082：可见行为变化只允许附录 G 所列）。
   - 修复后，超过 C 的标题从「被接受并扇出」变为「被拒绝、发送者断线」。按建议 A，合法客户端预计碰不到，但依据只是推断。
   - **建议：在附录 G 加 VC-9，由本单元按裁定写一行**：「`SET_PARTY_INFO` 的 inline-title 超过 C 字节时解码失败，发送者连接关闭（今天被接受，并扇出给队员与区域内的人）｜F-115」，证据为 P2、W1。T-006 期间已有同类先例（附录 A 第 4 行、VC-3，经裁定由执行单元改文字）。收口 §10 记为「文字已改，新增可见变化」。
   - 备选：不改 Spec，把它当作协议边界的输入校验，只写进收口 §11 的已接受差异。代价：RQ-082 字面上多出一项附录 G 之外的可见变化，交外部审核时要单独说明。

4. **是否在发送端或登记点加一层通用防线**（§5）。
   - **建议：T-006 内不加，新增 F-116 交 T-009 或主控方立项，不阻断 M-2。** 理由：普查后，客户端能控制的单帧扇出已有上界 80 ＋ C；剩下的超限路径（人数、导入放大）不是一条客户端请求能触发的，且已按裁定记为适用范围；两种通用防线都要改已定契约——(a) 与 RQ-036 字面相反，且会让在场与队伍状态静默失步；(b) 改 RQ-031(d) 的触发集合。
   - 备选：本单元内做 (b)。需要另行授权改 Spec RQ-031(d) 与登记点，超出本单元的 Owned boundary，建议另立单元。

5. **是否把「扇出字段必须有上限」写进 AGENTS.md 的新增协议固定动作**（§5 (c)）。
   - **建议：写，由主控方在提交 TASK-27 时补一句**（本单元的 Owned boundary 不含 AGENTS.md）。理由：本次普查是一次性的，之后新接线的协议没有判据守着；一句约定代价最小，评审时可以对照。
   - 备选：不写，只在 F-116 中记录，由 T-009 一并判定是否需要判据。

## 主控方裁定（执行前）

1. **标题上限 C ＝ 256 B，接受。**
   - 依据等级记为「推断」：现有证据只有下界（1 B、12 B）。256 B 取同类文本字段中最长的一类，目的是不误拒合法玩家。
   - 同时立项 F-117：ClientStatic 取证之后，若证明客户端上限更大，就必须上调。
2. **超长时沿用该解码器的既有做法：抛 `InvalidDataException`，由传输层关闭发送者连接，接受。** 被关闭的是攻击者自己的连接，不牵连第三方。
3. **在 Spec 附录 G 加 VC-9，接受，授权本单元写这一行**（RQ-082）。内容为：队伍标题超过 256 B 时请求被拒绝、发送者连接被关闭。Owned boundary 增加 `spec.md` 附录 G 这一行。
4. **通用防线（发送端或登记点）在 T-006 内不加，登记为 F-116，接受。** 交 T-009 或主控方另行立项。
5. **AGENTS.md 的约定不在本单元补。** 「客户端提供、会扇出的变长字段，解码器必须有上限」这一条登记为 follow-up，交 T-009，与 F-105 等其他 AGENTS.md 更新一起做。

## 主控方处置（执行中断后）

第 1 个执行者完成了基准核实与同类字段普查（只读，工作区无改动），在设计 W1（线级「超长标题使同区他人断线」的复现）时中断。普查结论：无上限、又扇出给他人的字段只有队伍标题，停止条件 1 未触发；宠物文本需要更正（有生产写入方，上限 12 B，每人 539 B 不变）。普查脚本与输出在 scratchpad `t27/`，由后续执行者落盘为 `logs/task-27-census.txt`。

**W1 改为不做线级复现**，证据形态调整如下（合同 Revision 2）：
- 不构造「发送超长标题、观察他人连接被关闭」的线级场景。F-115 的成因已由读码与 TASK-26 评审确证（`reviews/task-26-result-review.md` Round 1），本单元要证明的是修复本身。
- **基准先红的证据**改由解码器边界用例承担：P2（257 B 标题被拒绝）在基准上变红，原因是基准解码器接受了它。测试中直接写 VC-9 的值 256，不引用生产常量，避免基准上的编译失败被当作 RED。
- **W1 改为发送端或投影层的尺寸用例**：恰为上限的标题生成的名单帧为 80 ＋ 256 ＝ 336 B，单个观察者一次走进区域的发件箱增量不超过 F-113 重算后的每人上界。W2（恰为上限的标题照常扇出）保留。
- 变异 MT1～MT4 照旧，去掉上限后 P2 必须变红。
- 其余范围、裁定 1～5、停止条件不变。

## Result

执行轮次 1（2026-10-23，合同 Revision 2，基准 `8c531c9b`，未提交）。由第二位执行者接续：第一位执行者完成了基准核实与普查（只读）后中断；本轮复跑普查、落盘，再按「主控方裁定（执行前）」1～5 与「主控方处置（执行中断后）」完成其余全部。

- **Outcome**：done（待独立结果审查，Result review：required）。六条停止条件都没有触发；与计划或处置不同之处见 Why。
- **Changed**：
  1. **生产**（1 个文件，13 行新增）：`Server/src/Dfo.Server.Protocol/Game/Party/PartyCommandSlice.cs` 的 `SetPartyInfoDecoder`——常量 `internal const int MaximumTitleBytes = 256`（`:216`，前面 5 行 remarks 说明出处、依据等级与「不截断」的理由）；负长度检查之后、长度自洽检查之前加 `titleLength > MaximumTitleBytes` 即抛 `InvalidDataException`（`:245-249`），消息写明长度与上限。
  2. **测试**（3 个文件，都在 Owned boundary 内）：
     - `PartyCommandCodecTests.cs`：P1 `Set_party_info_accepts_a_title_of_exactly_the_maximum_length`（Theory 2 行：ASCII 256 B、85 个三字节字符 ＋ 1 个 ASCII ＝ 256 B）；P2 `Set_party_info_rejects_a_title_longer_than_the_maximum`（Theory 3 行：(257, 257)、(1,048,400, 1,048,400)、宣称 257 而不带标题），断言抛 `InvalidDataException` 且原因含 `256`；辅助方法 `TitledEdit`；
     - `PartyNotificationTests.cs`：P3 `Full_roster_with_a_maximum_length_title_is_eighty_bytes_plus_the_title`（名单帧 80 ＋ 256 ＝ 336 B，长度字段与标题字节的位置）；
     - 新建 `Server/tests/Dfo.Server.Networking.Tests/Integration/PartyTitleFanOutWireTests.cs`：W1 `A_maximum_length_party_title_keeps_a_walk_in_within_the_per_person_bound`（尺寸用例：队长在 1:1 建 256 B 标题的队伍，新到者走进区域，该 CMD 的名单帧恰 336 B、合计 ≤ 52 ＋ 670 ＋ 256 ＝ 978 B）、W2 `A_party_title_at_the_limit_reaches_a_player_walking_in`（新到者收到含该标题的 type-0 名单帧，两条连接围栏后都开着）。只读使用 `Characterization/` 的宿主、步骤与客户端。所有新用例直接写 256，不引用生产常量。
  3. **Spec**：`spec.md` 附录 G 加 VC-9 一行（裁定第 3 条）。
  4. **协议记录**（只追加）：`protocol/game/client-to-server/0x000C.md` 人工区末尾加「2026-10-23 标题长度上限（CurrentImplementation）」一节；`protocol/catalog.md` 0x000C 行状态栏末尾补一句；`protocol/game/server-to-client/NOTI-0x0009.md`「待验证」加一条（客户端 raw-dstr 读取的 `maxLen` 与编辑框字数）。没有推翻既有结论。
  5. **文档**：`verification/t006-closure.md` 末尾追加「## 12. 落地状态（TASK-27）」，前文不动；`verification/measurement/selection-outbound-bound.md` 末尾追加「### 落地状态（TASK-27）」，原文不动；`verification/follow-ups.md` 文首加 TASK-27 一行，关闭 F-115，更新 F-69、F-113、F-114，新增 F-116（通用防线评估）、F-117（客户端上限待 ClientStatic 取证）、F-118（AGENTS.md 新增协议固定动作补「扇出字段必须有上限」，交 T-009）。
  6. **证据**：`verification/logs/task-27-{verify,census,red-green,build-test,violations,isolated,new-x10,mutations,retrievals,diff-check,boundary,closure-refs}.txt`，共 12 份；一次性脚本在 scratchpad `t27/`，全文附在对应日志末尾。
- **Why**：
  1. **证据形态按「主控方处置（执行中断后）」调整，不按 Detailed Plan §4.3 的原 W1**：不做「超长标题使同区他人被 `OverLimit` 关闭」的线级复现。基准先红由 P2 承担（基准上 3 行全红：两行「No exception was thrown」，截断那行的拒绝原因不含上限）；W1 改为尺寸用例，基准上与修复后都绿；W2 保留。所以 Required check 2 中「W1 在基准上因 `OverLimit` 变红」与停止条件 2 的原文不再适用，正式全量里也没有计划预期的 `Game decoder SetPartyInfoDecoder threw` 行（0 条，没有用例发超长标题）。
  2. **P2 断言拒绝原因含上限**（计划写的是可二选一）：这样「检查排在自洽检查之前」有了守护——MT4 下截断那行变红；代价是 P2 第 3 行在基准上也是红（计划预期它在基准上绿），原因是基准把它当作截断拒绝、原因不提上限。
  3. **普查沿用第一位执行者的脚本**（scratchpad `t27/census.py`、`census_dump.py`），本轮只复跑一次，与其原输出逐字节相同（`diff` 退出码 0）。结论与 Detailed Plan §2.3 一致，只更正一处：宠物名有生产写入方（改名 ≤ 12 B），写入的就是 0x0002 名牌读取的 `CreatureText`（V10），每人 539 B 不变——写进 F-114 与 `selection-outbound-bound.md` 的落地状态。
  4. **收口引用核对脚本改了三处**：§8 按 §12 修订后的移交清单读；「本单元关闭」对 TASK-26、TASK-27 分别核对；修两个误报（`UTF-8` 里的 `F-8`、全大写的协议名被当作用例名）。都写在 `logs/task-27-closure-refs.txt` 文首。
  5. **正式全量之后改过一次注释的换行**（`SetPartyInfoDecoder` 的 remarks，只动文字）。变异之后的 `--no-incremental` 重建与之后的定向复跑（`PartyCommandCodecTests` ＋ `PartyNotificationTests` 89 通过、`PartyTitleFanOutWireTests` 2 通过）都在最终文本上；没有为注释重跑正式全量。
- **Checks**：
  1. **构建**：RED（只加用例）、GREEN、正式全量的第 1 部分、变异全部做完之后，四次 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 都是 0 警告 0 错误。
  2. **RED／GREEN**（`logs/task-27-red-green.txt`）：RED 时 `Server/src` 无改动，Protocol 6 ＝ 通过 3 ＋ 失败 3（P2 三行），Networking 2 ＝ 通过 2；GREEN 时 Protocol 6 ＝ 通过 6，Networking 2 ＝ 通过 2。W1 实测走进区域的 CMD 6 帧 649 B ＝ 52 ＋ 261 ＋ 336 ≤ 978，名单帧 336 B。
  3. **正式全量**（`logs/task-27-build-test.txt`）：8247 ＝ HttpApi 54 ＋ Pvf 80 ＋ Protocol 1752 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 181 ＋ Application 4538 ＋ Host 195（54 ＋ 80 ＝ 134；＋ 1752 ＝ 1886；＋ 477 ＝ 2363；＋ 970 ＝ 3333；＋ 181 ＝ 3514；＋ 4538 ＝ 8052；＋ 195 ＝ 8247）＝ 8239 ＋ 8 － 0。N 8 ＝ Protocol 6（P1 2 ＋ P2 3 ＋ P3 1）＋ Networking 2（W1、W2）：Protocol 1746 ＋ 6 ＝ 1752，Networking 179 ＋ 2 ＝ 181，其余六个项目不变。通过 54 ＋ 80 ＋ 1752 ＋ 477 ＋ 968 ＋ 181 ＋ 4520 ＋ 195 ＝ 8227，失败 2（基线 `DimensionDungeonTests` 两条），跳过 2 ＋ 16 ＝ 18；8227 ＋ 2 ＋ 18 ＝ 8247。挂起转储 0。表征测试 10 通过、0 跳过，`Characterization/` 与金样本无改动。日志信号与 TASK-26 逐条对照：应为 0 的七项中 `GameplaySelfWait` 为 1（同 TASK-26 的显示名），其余六项 0；bus 8、inbound 8、gm 2 逐条相同；「the drain tears it down」4（TASK-26 0）与「Outbound connection … closed」7 ＝ Failed 6 ＋ TimedOut 1（TASK-26 6 ＝ 5 ＋ 1）是进程内宿主停机时的时序：Networking 隔离复跑前者 0、后者 7，新用例 ×10 前者 0，TASK-25 的 Networking 隔离复跑前者为 1，本单元没有碰停机与排空路径。耗时 177 s，只作信息。
  4. **取证**（`logs/task-27-violations.txt`）：预期先写进日志开头。40 条 ＝ `bus-leaked-flow` 2 ＋ `network-wait` 1 ＋ `write-off-executor` 37（反例 24 ＋ 1 ＋ 1 ＋ 3 ＋ 1 ＋ 2 ＋ 1 ＝ 33，对照 7，33 ＋ 7 ＝ 40），反例类与对照以外 0，没有测试帧 0，`exe:` 0，退出码 0。与 TASK-26 相同。
  5. **普查**（`logs/task-27-census.txt`）：注册 184 ＝ 180 ＋ 4，不同的类 175 ＝ 184 － 9；定长 89 ＋ 有界 16 ＋ 开放 70 ＝ 175；保留可变字节 36 ＝ 6 ＋ 8 ＋ 22，36 ＋ 139 ＝ 175；去向 1 ＋ 1 ＋ 7 ＋ 5 ＋ 15 ＋ 7 ＝ 36；文本或社交类协议 161 ＝ 已注册 25 ＋ 未注册 136；锚点解析错误 0。无上限扇出的只有 `SetPartyInfoDecoder`。
  6. **变异**（`logs/task-27-mutations.txt`，每项备份、`cmp` 还原、前后 `git diff -- Server/src` 与 `-- Server/tests` 的 sha1 相同，最后 `--no-incremental` 重建 0／0）：MT1 去掉检查 → P2 3 行红；MT2 `>=` → P1 2 行与 W1、W2 红；MT3 上限减半 → P1 2 行、P2 3 行与 W1、W2 红，P3 绿；MT4 检查挪到自洽检查之后 → P2 截断那行红。每项都有指定的红用例。与计划的差别：MT1 下 W1 不红（W1 已改为尺寸用例），MT2 下 W1 也红，MT3 下 P2 也红（原因含 128），MT4 落在计划的第一个分支。
  7. **新增 ×10**（`logs/task-27-new-x10.txt`）：Protocol 6 例 ×10、Networking 2 例 ×10 全过；`PartyCommandCodecTests` 50 例 ×3、`PartyNotificationTests` 39 例 ×3 全过。
  8. **检索集**（`logs/task-27-retrievals.txt`）：R1 1 行；R2 3 行都在生产文件（测试不引用常量，按处置）；R3 7 行 ＝ 接口 1 ＋ 实现 1 ＋ 调用 5（计划写「定义 1」）；R4 2 行；R5 普查加法成立；R6 两个差异块都在 `SetPartyInfoDecoder`（208～283 行）内。
  9. **收口引用核对**（`logs/task-27-closure-refs.txt`）：用例 98、日志 27、F 编号 70、修订后的移交 F 编号 62，不符 0，退出码 0；F-115 在 `follow-ups.md` 中「已在 TASK-27 关闭」，F-113、F-114、F-116、F-117、F-118 未关闭且不是「信息」。
  10. **测试改动核对**（`logs/task-27-diff-check.txt`）：方法少 0、多 5（即 N 的 8 例）；断言行多重集合有变化的已有文件只有 `PartyCommandCodecTests.cs`、`PartyNotificationTests.cs`，各只多 3 行、少 0 行；`git diff -w` 删除行 0。
  11. **边界**（`logs/task-27-boundary.txt`）：`Server/src` 只有 `PartyCommandSlice.cs`（13 行新增，两个差异块在新行号 210～217、245～249）；`Server/src` 无未跟踪文件；`plan.md`、`AGENTS.md`、`Docs/` 无改动；`spec.md` 只多 VC-9 一行；`Server/tests` 只有 Owned boundary 列出的 3 个文件；`Characterization/`、`t006-test-dispositions.md`、`probe-tools/` 无改动。
  12. **隔离复跑**（`logs/task-27-isolated.txt`）：Protocol 1752 通过，取证 1 条（对照）；Networking 181 通过，取证 4 条 ＝ 对照 1 ＋ `OutboundRealSocketTests` 1 ＋ `OutboxRegistrationTests` 2；两者反例类与对照以外 0、`exe:` 0。F-69：比 TASK-26 正式全量慢 ≥ 9 s 的 7 条（Application 5、Host 2）逐条单独复跑 0.8～3 s，判读为满载拉长。
  13. **行尾**：`git -c core.whitespace=cr-at-eol diff --check` 无输出；改动的已有文件在基准与现在都是 0 个 CR；新文件 0 个 CR、以 LF 结尾。结束时 `dotnet build-server shutdown`，`pgrep -x yes` 为 0。
- **Remaining**（不阻断本结果）：
  1. F-117：256 B 没有客户端证据，需要用户在 Windows 端用 A21 客户端或内存转储取 ClientStatic；结果大于 256 B 时必须上调并重算 F-113。
  2. F-116：发送端或登记点的通用防线，交 T-009 或主控方立项，不阻断 M-2。
  3. F-118：AGENTS.md 的约定由 T-009 补（本单元的 Owned boundary 不含 AGENTS.md）。
  4. `plan.md` 的 T-006 行是否补写实际拆分（TASK-23～TASK-27），仍由主控方提交时处理（TASK-26 主控方裁定第 4 条）。
  5. T-006 完成与 M-2 进入都以本单元经结果审查并提交为准（`t006-closure.md` §12 第 5、6 条）。

## 主控方处置（评审后，提交前）

独立快审 verdict `approved`（`reviews/task-27-result-review.md`），4 条低严重度发现提交前处理完毕：
1. `t006-closure.md` §12 第 3 条与 F-116 的「单帧扇出最大 336 B」，改为限定在队伍名单帧，并注明在场快照单帧约 539 B、已在每人上界内。
2. §12 第 6 条补上 F-114、F-118，写明均不阻断 M-2。
3. `0x000C.md` 新节对 1 B 抓包的引用，改为指向「2026-09-13」一节。
4. 合同 Revision 标为 2，注明被取代的条目。
