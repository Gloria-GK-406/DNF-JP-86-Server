---
kind: rks-review
target: tasks/TASK-34.md
verdict: approved
---

> 文件级 verdict 取最新一轮（Round 2：approved）。Round 1 的 changes_required 保留在下文作为记录。

# TASK-34 结果快审（Independent Quick Review，第 1 轮）

- 被审对象：`tasks/TASK-34.md` 合同 Revision 1、Detailed Plan、「主控方裁定（执行前）」1～7、第 1 轮 Blocker、「主控方处置（Blocker 之后）」1～4、Result 第 2 轮（未提交）。
- 工作区 HEAD `51c294fa`，代码冻结点 `2bdb1afd`。被审改动：`AGENTS.md`（git 显示 2 个 hunk、3 处改动）、`verification/follow-ups.md`；新建 `verification/transition-mechanisms.md`、`verification/a-final-report.md`、`verification/t009-closure.md`、`verification/logs/task-34-*.txt` 13 份。
- 采用准则：`reviewing-work` 的 Task Unit 与 Deliverable 两份参考，范围限于本单元自己的产出。
- 只读：没有构建，没有跑测试；读了日志、原始输出、测试源码与 git 历史，并在 scratchpad 里做了只读检索与加法复算。

```yaml
kind: rks-review
verdict: changes_required
```

## 发现（按严重度）

### B-1（阻断）RQ-050 的实现精度残余没有进最终汇报，SC-6 的状态因此偏宽

- **位置**：`a-final-report.md` §4.2、§8「判据与证明的已知边界」、§9.2 SC-6 一行；`t009-closure.md` §7.1 SC-6 一行。
- **问题**：
  - `t007-closure.md` §1 把「RQ-050 关连接引起的生命周期 CMD 是登记关闭的 CMD 的子」与「RQ-012 继承链一半」记为**满足，附条件**；
  - 条件写在 §3 第 5 条与 §4 第 2 条：CMD 已经登记关闭、发送端还没执行时，传输层如果先因交付失败或客户端断开关了这条连接，这次断开仍按继承处理，而 RQ-050 末句字面要求新根；
  - `tasks/TASK-29.md:80` 明写：裁定第 5 条改了「RQ-050 澄清的实现精度」，「交外部审核时要能对上」。
  - 最终汇报里找不到这一项：§4.2 的解读表列了 TASK-29 裁定第 1、2、4、6(b) 条，唯独没有第 5 条；§8 的已知边界没有它；SC-6 记「满足」。
  - `t009-closure.md` §7.1 的 SC-6 一行在 Remaining gap 栏写「none（RQ-050 关连接继承一行的附条件……）」，一句话里既说没有缺口又说有附条件。
- **后果**：外部审核只看汇报，会在不知道这处偏离 Spec 字面的情况下批准；SC-6 的封存建议值也少写了一个条件。这正是本单元 Result review 理由里点名的错误类型。
- **修法**：
  1. §4.2 加一行，写明 RQ-050 末句、TASK-29 主控方裁定第 5 条、残余的形态，出处 `t007-closure.md` §3 第 5 条、§4 第 2 条；
  2. §8 已知边界加一条；
  3. SC-6 改为「满足，附条件」，条件写进 §9.2 表内；
  4. `t009-closure.md` §7.1 SC-6 的 Remaining gap 改写成条件与去向（外部审核时说明），不要写「none」。

### B-2（阻断）T6 给 B 封存用的检索式过宽，会命中 A 的常设机制

- **位置**：`transition-mechanisms.md` T6「B 封存时的检索」一栏。
- **问题**：检索式是 `grep -rn "_ = outboxes.Current" Server/src` 为 0。冻结点上它命中 5 处：
  - 两个 dispatcher 的作业起点：`NetworkMessageDispatcher.cs:51`、`NetworkConnectionLifecycleDispatcher.cs:35`。这两处就是 T6 本身。
  - 另外 3 处是登记点在执行器外时报程序错误的守卫：`OutboxAdapters.cs:70`（空批）、`OutboxAdapters.cs:164`（「Still a registration point」）、`NotificationBus.cs:79`。它们属于 §2 归为 A 常设机制的登记适配器与通知总线，B 归并之后仍然需要。
- **后果**：B 封存时这条检索不可能为零。B 要么判 SC-13 不满足，要么为了凑零删掉常设的守卫。清单的职责是让 B 准确知道要删什么，这条给错了对象。
- **修法**：检索范围收窄到两个 dispatcher 文件（或改用两处的成员名定位）；另外 3 处在 §2 写成相邻的常设机制，并说明不要求它们为零。

### B-3（阻断）`53c72543` 中 RQ-053(a) 的授权出处引错

- **位置**：`a-final-report.md` §4.1 第一行与 §4.1 末段；`logs/task-34-spec-revisions.txt` 文首与 `53c72543` 一节。
- **问题**：
  - 汇报给 RQ-050 与 RQ-053(a) 引了同一组出处：`reviews/task-02-result-review.md:64`、`:118`、`:129`。这三处都只关于 RQ-050 的 N-1；`:118` 的主控方处置原文是「N-1 写进 RQ-050 正文」。所以 RQ-050 一半的授权是充分的。
  - RQ-053(a) 的改动来自第 2 轮评审的 R2-N3（`:155-158`）。评审在那里写的是「建议主控方在 T-002 开始前写明」，结论句是「R2-N3 交给主控方在 T-002 前处置」（`:162`）。
  - 实际是执行方在 TASK-02 提交前自己写进去的（`tasks/TASK-02.md:45`「R2-N1、R2-N2、R2-N3 在提交前已处理」）。任务目录里没有找到主控方对 R2-N3 的单独裁定，只能把主控方提交 TASK-02 当作默认接受。
- **后果**：汇报写的是「授权出处在评审处置里」，外部审核按引用去核对，会以为 RQ-053(a) 也经过了主控方的明确处置。这一处需求文字修订的授权强度被写高了。
- **修法**：RQ-053(a) 一半改引 R2-N3（`:155-158`、`:162`）与 `tasks/TASK-02.md:45`，如实写明「主控方没有单独裁定，以主控方提交 TASK-02 视为接受」。该行的「授权出处」栏把 RQ-050 与 RQ-053(a) 分开写。

### B-4（阻断）新栈根的「不归因」写过了头，测试侧的交集没有后续事项承接

- **位置**：`a-final-report.md` §6 第一项（「读码无交集」）；`t009-closure.md` §2.2；`follow-ups.md`（没有对应条目）。
- **读码结论本身成立**：
  - `Fixture.Sql`（`AchievementTitleBookTests.cs:446`）每次调用都新建、使用、释放一个局部连接，数据库文件按 GUID 命名；
  - 用例体与 Fixture 的构造、使用、释放都在 `TestGameplay.RunAsync` 的同一个作业里按程序次序发生；
  - 原始输出 6081～6107 行的栈与此一致：第二次 `Sql` 调用（构造器 `:437`），`sqlite3_prepare_v2` 上的 `SafeHandle` 已被释放；
  - 所以「包装不改变这个 Fixture 连接的归属与次序」成立，并没有发现主控方处置第 1 条所说的那种缺陷。
- **问题**：
  1. **与 A 的改动有交集，只是不在失败路径上**。closure 自己的推断是：能释放这个句柄的只有进程级 `SqliteConnection.ClearAllPools()`。我复核了：A 在 `Application.Tests` 新增了 3 个调用点，22 → 25 个文件（`Execution/GameplayExecutorLifetimeTests.cs:139`、`Execution/TimerSourceHost.cs:80`、`OnlineUsers/SerialDisconnectTests.cs:91`）；`Server/src` 里没有调用点。照这个推断，A 的测试改动给这条偶发多加了触发源，可能提高了它的出现概率。§2.2 把这件事写成「不是新机制」，但汇报 §6 缩写成了「读码无交集」，丢掉了这层意思。
  2. **复现尝试 A 在构造上没有判别力**。30 次只跑 `AchievementTitleBookTests` 一个类，而这个类的 4 个 partial 文件都不在 25 个 `ClearAllPools` 文件里。按 closure 自己的推断，单独跑这个类本来就不可能复现。有判别力的只有 3 次整项目运行。汇报写「30 次加 3 次复现尝试均为 0 次」，读起来像 33 次独立证据。
  3. **没有去向**。登记进 AGENTS.md 只解决了「这次怎么判」；进程级清池与并行测试这件事（尤其 A 新增的 3 处）没有 F 编号。`follow-ups.md` 里现有的 F-38 是另一回事（Networking 隔离中测试主机崩溃，栈只有原生帧）。
- **后果**：按 AGENTS.md「清单外的新栈根不得直接归入，要另给不归因的明证」，这次登记的依据是「与失败路径无交集，且复现 0 次」，不是「与 A 的改动无交集」。汇报写成后者，等于把一个未排除的测试侧因素对外审写没了；同一偶发以后再出现，也没有地方承接。
- **修法**（不需要重跑，不需要撤回登记）：
  1. 汇报 §6 改写为：「包装与失败路径无交集；推断的触发机制（进程级 `ClearAllPools` 与并行测试类）在 A 之前就有，A 在同程序集新增 3 个调用点，可能提高了概率（推断，未观测）；有判别力的复现是整项目 3 次，单类 30 次在该推断下不能复现」；
  2. `t009-closure.md` §2.2 的复现一段补同一句限定；
  3. `follow-ups.md` 新增一条（例如 F-130），去向「交主控方另行立项（测试基础设施）」：`Application.Tests` 有 25 个文件调用进程级 `ClearAllPools`，A 新增 3 个；可改为按路径 `ClearPool`，或把这些类放进不与其他类并行的 collection。文首 TASK-34 一行与 `t009-closure.md` §6 的计数随之更新。

### L-1（非阻断）Spec 修订的 hunk 数是 52，不是 53

- `git log -p -U0 b76504bb..HEAD -- spec.md` 共 52 个 `@@`。按提交是 5 ＋ 1 ＋ 2 ＋ 1 ＋ 1 ＋ 5 ＋ 1 ＋ 1 ＋ 2 ＋ 2 ＋ 1 ＋ 18 ＋ 1 ＋ 3 ＋ 8 ＝ 52，`a-final-report.md` §4.1 表的 hunk 数栏相加也是 52；`logs/task-34-spec-revisions.txt` 里逐行列出的 hunk 也是 52 行。
- 脚本本身不输出总 hunk 数，「53」是手写的。要改的地方有三处：日志文首、汇报 §4.1 首句、TASK-34 Result Check 11。正是 AGENTS.md「小计不写出加法」那条要防的形状。

### L-2（非阻断）T3「新代码不要依赖」一栏与 AGENTS.md 不一致

- `transition-mechanisms.md` T3 写「也不要依赖这几个标记接口（`AGENTS.md:360-363`）」。但 `AGENTS.md:363` 只说这些接口「是过渡形态」，「新代码不要依赖」后面只跟着「一条命令一套管线」。而且今天新命令要得到 45、58 的行为，就必须实现这些接口。
- 修法：两边对齐，例如写成「新代码不要依赖这些接口在 B 之后仍然存在」。

### L-3（非阻断）封存数据的 Constraint 栏没有逐条依据

- `t009-closure.md` §7.1 列出了根契约七条 Constraint，但每行只填一个「aligned」，看不出逐条怎么判的。「纯搬迁与语义改动分开提交」「一次到位删净」两条，在 R3 保留过渡机制之后尤其需要一句依据。
- 建议补一张七条 × 依据的小表，最终值仍由主控方定。

### L-4（非阻断）AGENTS.md 改动的计数说法不一致

- TASK-34 Result 写「共 3 个 hunk」，git 显示的是 2 个 hunk、3 处改动；`t009-closure.md` §1 写「两处」。
- 统一写成「2 个 hunk、3 处改动」即可。

## 已核实无问题的项目

| 项 | 核对方式与结论 |
|---|---|
| 冻结与边界 | `git diff 2bdb1afd -- Server Tool` 为空；`Server/`、`Tool/`、`protocol/`、`Docs/`、`Document/` 在 `git status` 中没有条目；`diff --check`（cr-at-eol）无输出；新文件与 follow-ups.md 无 CR、无行尾空格 |
| AGENTS.md 改动范围 | 只有 T3 一条（补四个标记接口名，依据 TASK-33 裁定第 9 条）与栈根表（标题改为四条，加一行，依据处置第 2 条）；「见于」栏的类名与原始输出一致（方法在 partial 文件 `AchievementTitleBookMirrorTests.cs` 中，类名是 `AchievementTitleBookTests`）；「过渡」6 处与 T1～T6 一一对应 |
| 正式全量 | 逐项目 57 ＋ 80 ＋ 1752 ＋ 477 ＋ 970 ＋ 188 ＋ 4594 ＋ 197 ＝ 8315；通过 8294、失败 3、跳过 18，加起来 8315；失败 3 的身份与原始输出一致 |
| 日志信号归因 | 发送端 8 对 ＝ 重置 5 对 ＋ TimedOut 1 对 ＋ 进程内宿主 2 对；drain 3 行逐行给了原始行号；那 1 对 Warning `Failed` 是 `SocketException (32)`，属 F-126 的形态，不是 F-127 的 |
| AC-009① | 原始输出第 3932 行确实是「已通过」挤在日志前缀之后；193 个方法、332 行，以及对 TASK-33「331 行」的订正，依据成立 |
| 测试数量加法 | 39 段逐段闭合、首尾相接；ΣN 30 项 ＝ 596，ΣD 8 项 ＝ 153，7872 ＋ 596 － 153 ＝ 8315；抽查 TASK-04、TASK-22、TASK-23、TASK-34 的逐项目和；基线逐项目（Networking 97、Application 4342）与 `baseline.md` 一致；TASK-01、TASK-04、TASK-05 的出处缺口如实标注 |
| 测试文件四类 | 218 ＋ 67 ＋ 41 ＋ 30 ＝ 356；b、c 共 71 个文件都有处置记录点名（粒度是文件，已写明）；a2 中 4 个文件没有点名，已如实列出；7 个删除文件逐个指到处置；codec 77 对 82、`ComposeAsync` 7 对 9 的差额如实写为不可解释 |
| 证据用例 | 抽查 VC-1～VC-10、AC-001 的 9 个证据用例名，在原始输出中都有「已通过」；AC-002 网络一半的用例确实断言了程序错误、取证与带链日志 |
| AC 状态 | AC-001～AC-006、AC-015～AC-017、AC-019～AC-022 的「满足」都能对到收口记录：AC-006 由 TASK-33 §12 从「未满足」改为「满足」；AC-009 的附条件按 §3.3 解除；AC-016 在 `t007-closure.md` 本身就是「满足」（附条件在 RQ-050 那一行，见 B-1） |
| 三项默示决定 | RI-4、RI-8、RI-9 的复述与根契约 `TaskContract.md:24`、`:28`、`:187`、`:191`、`:192` 原文一致 |
| Spec 修订（抽查） | 15 个提交、52 个 hunk 逐个对过归类行。核对了 `1eea890f`、`bfbcf012`、`6331b23a`、`c3115f9a`、`f13e612a`（5 个）、`0c9c59c8`、`09e4983c`、`50d7bce4`（2 个）、`1ee1c91e`（2 个）、`f7bf0aa3`、`a9c79e98`、`122576d5`（3 个）与 `51c294fa`（8 个）中的 hunk，共 30 个以上。授权出处除 B-3 外都对得上 |
| 过渡清单其余各项 | T1～T5 的检索式在冻结点可运行，命中数与清单的描述相符（T1 2 处、T3 行为 5 处与标记接口 139 行、T4 8 处）；S7 的 101 个类型逐个有归类，99 ＋ 2 ＝ 101；登记适配器归为常设机制、分流键归为授权预留，理由成立；`EffectOperationLedger`、`SelectedSessionSafety` 不进清单的理由成立 |
| follow-ups 终态 | 解析出 129 行 ＝ 已关闭 80 ＋ 未关闭 35 ＋ 信息 11 ＋ 部分关闭 3；49 条不是「已关闭」的都带「〔A 封存：→ …〕」；F-05、F-10 的另一半确实在 F-82 正文中；F-69 的 A 一半已关闭；F-129 的行号指向 Channel 那一支的降级；F-118 按处置第 4 条写成无编号遗留 |
| 封存数据 | §7.2 给了 spec-001 一行的 Result 路径与两份必引文件；§7.3 给了封存前要再核的事项。除 B-1 与 L-3 外，没有发现缺项 |

# Round 2：返工定点复核（Independent Quick Review，第 2 轮）

- 被审对象：`tasks/TASK-34.md` Result「第 1 轮评审后修改」一节，以及「主控方处置（第 1 轮评审后）」第 1～5 条。第 3 条含主控方对 RQ-053(a) 的追认。
- 合同仍为 Revision 1；HEAD `51c294fa`；冻结点 `2bdb1afd`；改动仍未提交。
- 范围：只看 Round 1 各项发现的落点，以及由修改直接带来的回归。Round 1 已核实无问题、这次没改的项目，沿用 Round 1 的证据。
- 只读：没有构建，没有跑测试；对 follow-ups 重新解析计数，对 spec 修订重新数了 hunk，并对几份文件做了残留说法检索。

```yaml
kind: rks-review
verdict: approved
```

## 逐项核对

| 项 | 结论 | 依据 |
|---|---|---|
| B-1 RQ-050 残余 | 已解决 | 汇报 §4.2 新增「RQ-050 末句的实现精度」一行，出处 TASK-29 裁定第 5 条、`tasks/TASK-29.md:80`、`t007-closure.md` §3 第 5 条与 §4 第 2 条；§8 已知边界新增一条，写了去向；§9.2 SC-6 改为「满足，附条件」，条件写在表内；`t009-closure.md` §7.1 SC-6 的 Remaining gap 改为条件与去向，不再写「none」 |
| B-2 T6 检索式 | 已解决 | 检索收窄到 `NetworkMessageDispatcher.cs` 与 `NetworkConnectionLifecycleDispatcher.cs`，冻结点上各命中 1 处，正是 T6 本身。`OutboxAdapters.cs:70`、`:164` 与 `NotificationBus.cs:79` 在 §2 新增一行，写成「登记点的执行器守卫」，归 A 的常设机制，并写明它们不要求为零 |
| B-3 `53c72543` 授权 | 已解决 | 汇报 §4.1 拆成三项。① RQ-050：引 `:64`、`:118` 主控方原话与 `:129`，判为授权充分。② RQ-053(a)：引 R2-N3（`:155`、`:162`）与 `tasks/TASK-02.md:45`，如实写明当时没有单独裁定，强度为「提交即接受＋本次追认」。③ 附录 A、D：引 TASK-02 合同。追认出处 `tasks/TASK-34.md`「主控方处置（第 1 轮评审后）」第 3 条存在，原文与汇报的引述一致。`logs/task-34-spec-revisions.txt` 文首同步改正 |
| B-4 新栈根依据 | 已解决 | 汇报 §6、§8 与 `t009-closure.md` §2.2 都改为「与失败路径无交集、复现 0 次」，并加两点限定：A 新增 3 处 `ClearAllPools` 与推断的机制有交集；单类 30 次在构造上不具判别力。F-130 已建，内容与三个文件行号和 Round 1 复核结果一致，去向是「交主控方另行立项（测试基础设施）」；文首一行与 `t009-closure.md` §6 已同步。`logs/task-34-isolated.txt`、`logs/task-34-build-test.txt` 的判定段也已改写 |
| L-1 hunk 数 | 已解决 | 我重数 `git log -p -U0 b76504bb..HEAD -- spec.md`，共 52 个 `@@`。日志里逐行列出的 hunk 也是 52 行，日志末行由脚本输出「hunks 52 ＝ 5 ＋ … ＋ 8」。汇报 §4.1 首句与 Result Check 11 都写出了加法 |
| L-2 T3 措辞 | 已解决 | 改为：今天新命令要得到 45、58 号行为，仍须实现这些标记接口；不要假设它们在 B 之后仍然存在。与 `AGENTS.md:363` 不再冲突 |
| L-3 Constraint 依据 | 已解决 | `t009-closure.md` §7.1 末新增七条约束的依据表，逐条写了依据与建议值 |
| L-4 AGENTS.md 计数 | 基本解决 | Result 与 `t009-closure.md` §1 都统一为「2 个 hunk、3 处改动」。残留一处见 R2-L1 |

## 计数

- **follow-ups**：重新解析得 130 行 ＝ 已关闭 80 ＋ 未关闭 36 ＋ 信息 11 ＋ 部分关闭 3，与文件中写的加法一致。不是「已关闭」的 50 行都带「〔A 封存：→ …〕」，没有去向的为 0。
- **未关闭 50 条的去向**：按标记逐条归类，得 子 Spec B 15、子 Spec B ＋ 主控方 1（F-128）、后续性能优化 7、交主控方另行立项 13、用户 1、外部审核 2、信息 11；15 ＋ 1 ＋ 7 ＋ 13 ＋ 1 ＋ 2 ＋ 11 ＝ 50。
  - 「交主控方另行立项」的 13 条：测试基础设施 5（含 F-130）、取证缺口 3、无括注 3、F-116 (a)(b) 1、F-125 1。
  - 与 `t009-closure.md` §6、`logs/task-34-followups.txt` 文首、Result Check 12 都一致；日志另写出了 128 → 130 的前后加法。
- **spec hunk**：52，三处一致。

## 三份文件之间的一致性与残留说法

我在三份主文件、TASK-34、follow-ups 与相关日志中检索了以下旧说法：「53 个 hunk」「读码无交集」「30 次加 3 次」「49 条」「129 行」「3 个 hunk」、旧的 T6 检索式（`"_ = outboxes.Current" Server/src`），以及 SC-6 的「none」。

- 主文件里一处都没有残留。
- 「无交集」只以「与失败路径无交集」的限定形式出现。
- `logs/task-34-spec-revisions.txt` 第 5 行提到「53」，那是更正说明本身，不是残留。

## 三项检查

| 检查 | 结果 |
|---|---|
| 收口引用 | `logs/task-34-closure-refs.txt` 已重跑。transition 40 处、a-final-report 9 处、t009-closure 203 处 `文件:行` 引用都解析得到，unresolved 0。新增的 `OutboxAdapters.cs:70`、`NotificationBus.cs:79`、`tasks/TASK-29.md:80` 都在其中 |
| `diff --check`（cr-at-eol 口径） | 我当场重跑，无输出。四份新写文件都是 CR 0、行尾空格 0 |
| AGENTS.md 自身规矩 | `logs/task-34-transition.txt` 第 16～17 行写明已重跑，禁用模式 0。这一轮 AGENTS.md 没有改，`git diff --stat` 仍是 9 行（＋6／－3），与 Round 1 相同 |

## 冻结

- 我当场跑了 `git diff 2bdb1afd --stat -- Server Tool`，输出为空。
- `git status --short` 中没有 `Server/`、`Tool/`、`protocol/`、`Docs/`、`Document/` 下的条目。
- 改动只有 `AGENTS.md`、`follow-ups.md` 与 Owned boundary 内的新文件，外加本评审文件。

## 非阻断残留（不影响 verdict，提交前可顺手改）

- **R2-L1**：`logs/task-34-boundary.txt` 第 2 行仍写「AGENTS.md hunks (3)」，与已统一的「2 个 hunk、3 处改动」不一致。
- **R2-L2**：`logs/task-34-isolated.txt` 第 52 行写「no intersection found by reading」。这句是在复述主控方处置第 2 条的触发条件，前面第 35～37 行已经写了限定，不会误导；但单独引用这一行时会丢掉限定，可以改成「no intersection with the failure path found by reading」。

## 结论

Round 1 的四项阻断与四项非阻断都已落到所指位置，改法到位；计数闭合，三份文件互相一致，冻结点之后 `Server/`、`Tool/` 没有改动。本轮 `approved`，范围只是 TASK-34 的这次本地快审，不代替封存前的完整结果评审与最终核验。
