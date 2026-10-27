---
kind: rks-review
target: tasks/TASK-01.md
verdict: approved
rounds:
  - round: 1
    result_version: 3
    verdict: changes_required
  - round: 2
    result_version: 4
    verdict: approved
---

# TASK-01 结果快速审查（Independent Quick Review，第 1 轮）

## 审查对象与范围

- 契约：`tasks/TASK-01.md` Task Unit Contract Revision 1；Result 第 3 版（覆盖前两版）。
- 改动：HEAD `b76504bb` 加未提交的工作区内容——`Server/tests/Dfo.Server.Networking.Tests/Characterization/`（12 个 `.cs`、`Golden/` 15 个 `.hex`）、`Dfo.Server.Networking.Tests.csproj` 的 diff、`verification/baseline.md` 与 `verification/logs/` 的 5 份日志。
- 依据：spec RQ-080、AC-019、附录 D/F/G/H；plan「共享决定」与 T-001；AGENTS.md。
- 采用的准则：reviewing-work 的 Task Unit 快速审查，加上 Code 准则（限本单元的局部缺陷）。
- 视为已定的主控裁定：(b1)(b2) 拆成常绿版（前缀比对＋已关闭）和跳过的 strict 版，由 T-006 启用。
- 本轮没有提供挑战集。

## 结论

`changes_required`。有 1 项阻断：XO-1/XO-2 的跨连接到达先后断言在顺序反转时仍然通过，Result 中「XO-1/XO-2 已断言」的结论没有依据。其余部分（硬约束、帧级比对、掩码、金样本、日志数字）没有发现阻断缺陷。

## 阻断发现

### B-1　XO-1／XO-2 断言在顺序反转后依然是绿的，不能作为跨连接顺序的证据

**证据（本地反转实验，改动已恢复）**

1. 临时修改两处产品代码：
   - `DungeonEntryProjectionDelivery.cs` 把 `.OrderBy(value => value.Session == requester ? 1 : 0)` 改成 `? 0 : 1`，让领队先收到；
   - `LeaveDungeonSlice.cs` 把领队自己的 `EplpCommandResponse` 发送挪到同伴循环之前。
2. 执行 `dotnet build Server/tests/Dfo.Server.Networking.Tests -c Release`，然后执行 `dotnet test ... --no-build --filter "FullyQualifiedName~PartyCharacterizationTests"`，共 5 次。
3. 结果：5 次都是 4/4 通过，(c) 与 (d) 都没有报 `XO-1:`/`XO-2:`。输出在 `/private/tmp/claude-501/-Users-puxianguo-project/19adbc1e-6ba1-4530-bca0-dcab5d727d8a/scratchpad/xo-reversed-1.txt`～`xo-reversed-5.txt`。
4. 恢复：两个文件的 sha1 与备份一致（`572138cc…`、`376a6b3f…`），`git status --short Server/src` 为空。重建后 `Dfo.Server.Protocol.dll` 的 sha1 为 `59a3c6fb…`，与 Application.Tests、Protocol.Tests 输出目录中未受影响的副本相同。恢复后表征测试跑了一次：8 通过，2 跳过。

**原因**（`CharacterizationSteps.EnterDungeonAsync` 与 `PartyCharacterizationTests` 中 (d) 的写法）：测试先阻塞读领队，直到读到它的帧，然后才用 `ReadAlreadyArrivedAsync` 非阻塞地查队员的 socket。顺序反转时，服务端在领队那一帧之后紧接着就发队员的帧，而本机回环上测试线程赶到检查点之前，这一帧早已进了队员的接收缓冲。所以这条断言只在队员的帧晚到很多时才会失败，与「谁先」无关。逐连接金样本本来就不约束跨连接顺序，因此现在没有任何表征用例能发现 XO-1/XO-2 被破坏。

**后果**：T-004（入口迁入执行器）、T-006（换发件箱、RQ-033 有序条目）、T-008（归并进本/离本）正是可能改变跨连接投递次序的步骤，而 plan 的 Verification 把 T-001 表征测试定为「全程通过」的行为不变证据。按现在的写法，XO 被改坏时这批测试仍是绿的。这正是契约 Result review 理由里点名要防的那类缺陷；Detailed Plan §2 也要求这项断言能在请求者先到时失败。

**最低限度的修正（二选一，由控制方定）**

- (i) 把跨连接检查改成能分辨先后的形式，并在 Result 中附上一次反转实验（反转后必须红，恢复后必须绿）。改法举例：两个连接各用一个后台读循环，在收到数据块时打单调时间戳，比较两条目标帧的到达时刻；或者在一个轮询循环里同时等两个 socket，记录哪一个先读到目标帧。同时要说明它在基线满载下是否稳定。Remaining 1 已经指出，基线的 `SendAsync` 返回只表示帧进了发送管道，两条连接的发送循环互相独立，所以基线上的到达顺序本身未必有保证。
- (ii) 如果在基线上做不出既能分辨、又稳定的断言，就按 executing-tasks 如实降级：在 Result 中撤回「XO-1/XO-2 已断言」，写明这两项由 AC-012 的单元/集成测试在 T-003/T-006 承接，交控制方处置。不要保留一条通过但不能失败的断言来充当证据。

## 非阻断发现

### N-1　(b1) 常绿版在 T-002～T-005 期间实际上不守护拒绝对的内容

前缀比对允许读到 0 帧。日志 `characterization-load.txt` 中 A2＋C＋B 三个系列共 50 次，读到 0 帧的有 5 次。因此在 T-006 启用 strict 版之前，「拒绝对不再发出」这类回归不会让 (b1) 变红。这是主控裁定带来的代价，不判为缺陷。

建议：后续每个 Task 隔离运行时，检查测试输出里的 `refusal frames read before the close: N of 2`。负载系列 B 中隔离运行 20/20 都是 2 帧；隔离运行出现 0 帧时应当作信号追查，不能只看绿灯。(b2) 同理，看 `selection frames read before the close`。

### N-2　b2-strict 「断言不需修改」的说法与 VC-1/VC-2 可能冲突

Remaining 2 写的是「启用办法：去掉 Skip，断言不需要修改」。但 T-008 把选角归并成一条协议 CMD 后，任务同步失败如果以未处理异常结束 CMD，按 VC-2，同一工作单元里登记的入场批次不会发出。到那时 b2-strict 「整份入场批次先于关闭到达」的期望可能需要按附录 G 调整。建议把措辞改为「T-006 启用；之后如有改动，按附录 G 逐条对应」。

### N-3　(b) 的「同伴收到的清理通告」在基线上无法覆盖

请求者总是落在不共享在场状态的城门区域，所以同伴金样本只钉住了「同伴什么都没收到」。这在基线上走不通，Result 的 Why 与测试注释也写明了。清理通告扇出实际由 (g) 覆盖。建议在 Result 的 (b) 行里明确写一句「同伴清理通告不在 (b) 覆盖范围，由 (g) 承担」，避免下游误读。

### N-4　两处掩码本可以用允许的接缝固定，而不必掩掉

Detailed Plan §2 要求「能固定的优先固定」。下面两处都可以用契约允许的直接写 SQLite 在选角前改成固定值：

- CMD `0x0004` 的 `created_at`：执行 `UPDATE characters SET created_at = …`；
- USERINFO 的 3 处 `InstanceValue`：改写 `items.item_core` 中对应的 4 个字节。

现在的掩码每项只有 4 字节、理由清楚、不覆盖整帧，强度损失有限，不作阻断。

### N-5　测试宿主复制了 `Program.cs` 的组合，后续改组合根时会悄悄分叉

`CharacterizationHost` 按 `Dfo.Server.Host/Program.cs` 手工重组，差异有三处：没有登记 `IServerModule` 单例（只影响启动日志）、没有 GmWeb、用的是 `new HostBuilder()` 而不是 `CreateDefaultBuilder`。如果 T-002/T-004 把执行器、停机顺序之类的接线放进 Host 组合根，而不是放进各模块，表征宿主就会缺这部分。建议后续 Task 只要改了 `Program.cs`，就同步核对 `CharacterizationHost`。

另外，csproj 新增了对 `Dfo.Server.GameServer` 与 `Dfo.Server.HttpApi` 的项目引用，略超出 Owned boundary 原文「必要时调整 csproj 以复制数据文件」。理由在 Result 的 Changed 一节已写明，不影响分层。

### N-6　CI 中没有 Script.pvf 时，这批测试全部跳过

全部用例都是 `[PvfFact]`。本地日志证明它们真的执行了，但后续 Task 引用「表征测试通过」时，必须是有 PVF 的本地运行，并核对执行数为 8，不能只看 CI 绿灯。

## 已核对、未发现问题的项

- **硬约束**：测试代码中没有 `ICommandBus`、命令类型、消息 handler、门、`INotificationBus`、`IEventService`，也没有替换 dispatcher 或 sender（逐文件 grep 并阅读）。
  - 故障接缝 `IExpiredCharacterItemCleanupDomainService`、`ISelectCharacterQuestEntryDomainService` 都是 Abstractions 中的 `*DomainService`，原注册都是 Singleton（`ApplicationModule.cs:759/765`），装饰器以 Singleton 重注册，生命周期不变；两者都不在附录 F/H 中。
  - `DeterministicLayouts` 装饰的是 `IDungeonStructureRuleService`（同为 Singleton，`ApplicationModule.cs:295`），只替换 seed 参数，属于 §2 认可的「固定种子」，不是故障注入，也不在附录 F/H 中。
  - 状态准备只走线上协议和启动器 HTTP。(e) 直接写 SQLite（经验值与道具 4183），原因写在测试代码里。
- **帧级断言**：逐连接逐帧逐字节比对，步骤标记参与比对，帧数和帧序都约束到了。掩码表共 5 项，每项 4 字节且附理由；`Hex()` 在运行期拒绝覆盖帧头或整帧的掩码。
  - 浮动帧只有 NOTI `0x0015` 一条规则：取锚点 CMD `0x0239` 之后第一次出现的 `0x0015`，字节和条数照比。(a) 的 62 帧类型检查截止在 `0x0016`，而 `0x0016` 位于 `0x0239` 之前（金样本第 088/091 帧），所以浮动帧不会落进那段检查。
  - `lastStepMayEndEarly` 只放宽最后一步的尾部：观测必须越过最后一个 `## ` 行，且是逐行前缀。
- **(a)**：硬编码的 60 个身份与 `SelectCharacterAcceptedTranscriptTests.AcceptedStaticIdentities` 的 62 个去掉 `0x0245`、`0x0077` 后逐项相等（脚本比对，结果 True）。
- **金样本是预期路径的产物**：
  - (b1) 请求者：`01/0004` body `0002`，加 `01F7`「Character entry failed…」；
  - (c) 领队：`000F`→…→`001D START_MAP`→`0024`，没有 `01/0010` 拒绝；
  - (e) 领队：`0012` 扣票→…→`008E`→`008F`，没有拒绝包；
  - (d) 队员：`01/0048`，之后是回城投影。
  以上都与 Result 的对照结论一致。
- **日志数字**：
  - `baseline-build-test.txt` 中构建 0 警告 0 错误，各项目数字与 `baseline.md` 一致，`[FAIL]` 恰好 2 处（第 8655、9101 行）。
  - `characterization-baseline.txt` 三次都是 10 个用例：8 通过、2 跳过。
  - `networking-isolated.txt` 共 107 个：105 通过、2 跳过。
  - `full-with-characterization.txt`：总数 50+1694+80+475+969+106+4342+165 = 7881；通过 50+1694+80+475+967+105+4324+165 = 7860；跳过 2+1+16 = 19。
  - 负载日志中 b1 帧数的分布：0 帧 5 次、1 帧 2 次、2 帧 43 次，合计 50 次，与 Result 中 A2（2/0/8）、C（3/2/15）、B（0/0/20）三组相加一致。
- **未越界**：`Server/src` 与既有测试文件均无改动；csproj 的 diff 只有项目引用、链接文件和复制金样本三项。

## Round 2

```yaml
kind: rks-review
verdict: approved
```

### 范围

- 审查对象：Result 第 4 版，以及返工后的工作区（HEAD 仍是 `b76504bb`，改动未提交）。
- 只看四件事：B-1 的处置、N 项是否写入、新一轮检查的数字与日志是否一致、返工有没有引入新问题。
- 主控方事先裁定：方案 (i) 在基线上不稳定时改走方案 (ii)。AC-012 将在发送端「交给传输层」的层面断言顺序。本轮把这两点视为已定。

### B-1：已解决

**测量数据支持结论。** 依据是 `verification/logs/xo-arrival-measurement.txt`。逐项核对了样本个数和非正值个数，都与日志一致：

| 情形 | 样本数 | lead 非正值个数 | 实际值 |
|---|---:|---:|---|
| XO-1 空闲 | 10 | 0 | 0.0031～0.0450 ms，与 Result 中「3～45 µs」相符 |
| XO-1 负载 | 20 | 6 | 其中负值 4 个、0.0000 两个 |
| XO-2 空闲 | 10 | 4 | 负值 1 个、0.0000 三个 |
| XO-2 负载 | 20 | 14 | 负值 5 个、0.0000 九个 |

在基线上，客户端观察到的先后顺序会出现反向和并列，所以做不出「基线为绿、反转后必红」的断言。改走方案 (ii) 有依据。

**无残留的无效断言。** 测试代码中已经没有 `ReadAlreadyArrivedAsync`、`client.Available`、`XO-1:/XO-2:` 断言，也没有 `before` 计数（grep 为零命中）。`EnterDungeonAsync` 改为单个谓词：读到入场帧或 `01/0010` 拒绝包为止，仍然断言不是拒绝包，然后对两个连接做栅栏。(d) 的离本改为发送请求后直接对两个连接做栅栏，逐连接的转写比对不变。金样本文件没有改动，时间戳仍是 18:25。

**改名与注释如实。** (c)(d) 改名为 `..._produces_the_recorded_transcripts`，两处 remarks 都写明 XO 不断言、原因是什么、由谁承接。(e) 补了一句注释：附录 D 对进塔没有顺序要求。

**Result 不再声称 XO 已断言。**
- Outcome 单列一条「没有断言，已撤回」；
- (c)(d) 两行写的是「只逐连接比对……不做断言」；
- Checks 第 4 行如实写明反转实验没有做；
- Remaining 1 交代了承接方。

### N 项：都已按处置写入

| 项 | 写入位置 |
|---|---|
| N-1 | Remaining 4，给出帧数核对口径：2 of 2 和 84 |
| N-2 | Remaining 3，按附录 G 逐条对应 |
| N-3 | (b1) 行中写明「(b) 不覆盖同伴清理通告，由 (g) 覆盖」 |
| N-4 | Why，附接受理由 |
| N-5 | Why 与 Remaining 6 |
| N-6 | Remaining 5 |

### 新一轮检查：数字与日志一致

- `characterization-baseline.txt`（20:02）跑了 3 次，每次都是 10 个用例：8 通过、2 跳过。跳过的只有两条 strict。每次输出 `2 of 2` 和 `84`，(c)(d) 使用新用例名。
- `networking-isolated.txt`（20:02）共 107 个：105 通过、2 跳过，0 警告 0 错误。
- 我在当前代码上做了独立复核：Release 构建 0 警告 0 错误；`--filter "FullyQualifiedName~Characterization"` 结果为 8 通过、2 跳过，输出 `2 of 2` 和 `84`。`Server/src` 没有改动。输出在 `/private/tmp/claude-501/-Users-puxianguo-project/19adbc1e-6ba1-4530-bca0-dcab5d727d8a/scratchpad/round2-run.txt`。
- 负载系列是在返工前的代码上跑的。Result 说明返工只删除了不会失败的检查、只改了用例名，读帧和比对逻辑都没动。我对照代码确认属实，结论仍可沿用。

### 非阻断（本轮新增）

- **R2-1**：(c) 的 remarks 写的是 XO-1 在负载下「went the wrong way in 6 of 20」。按日志，这 6 次是 4 次反向加 2 次并列（0.0000）。(d) 的 remarks 写的是「wrong way or tied」，措辞准确。建议 (c) 也改成「wrong way or tied」。
- **R2-2**：这次测量没有区分两种来源：服务端两条连接的发送循环各自刷写，以及客户端两个接收线程的调度抖动。Remaining 1 把原因归到前者，这个推断合理，但这份数据本身没有证明它。不影响走方案 (ii) 的结论：两种来源中只要有一种存在，就做不出稳定的客户端到达先后断言。
- **R2-3（交给控制方）**：Detailed Plan §3 的 (c)(d) 行仍把 XO 列为关键断言，契约没有修订；偏离已经在 Result 中记录，并且有主控方的预先裁定。T-003 和 T-006 的 Task 契约需要明确写出 XO-1/XO-2 由 AC-012 承接，以及在「交给传输层」层面断言的口径，免得这两项在交接中落空。

### 结论

`approved`。本轮没有阻断项。这个结论只覆盖 TASK-01 这一单元的局部检查，不等于整体完成；它也不代表 XO-1/XO-2 已有证据，这两项仍待 T-003/T-006 的 AC-012 测试给出证据。
