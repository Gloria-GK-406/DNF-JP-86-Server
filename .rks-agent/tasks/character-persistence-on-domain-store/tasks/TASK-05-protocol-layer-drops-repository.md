# TASK-05 — 协议层不再直接持有人物持久化契约

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 协议层的角色选择阶段命令处理器不再直接注入人物的持久化契约，改用业务层入口。
- Referenced spec requirements: RQ-010
- Referenced plan sections: `plan.md` 的 `### T-005`
- Owned boundary: BD-008, BD-012
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs`
  - `Server/tests/`
- Dependencies: TASK-04
- Required dependency outputs: TASK-04 提供的业务层入口。
- Acceptance conditions:
  1. 协议层不出现人物的持久化契约。
  2. 协议用例全部通过，既有用例只作因依赖变化的机械调整。
  3. 对外可观察行为不变。
- Task-specific authorization: 属根 TaskContract `task` 授权范围内。
- Required methods: 机械替换加回归。不得借此改变角色选择各阶段的时序或语义。
- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-014 | `grep -rn "ICharacterEntryMutationRepository" Server/src/Dfo.Server.Protocol --include=*.cs` | 零命中 |
| CK-015 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests -m:1` | 协议用例全部通过 |

- Required evidence: 两条检查的实际输出、以及时序未变的说明。

### 共同约束

- 全程不产生任务级提交。工作保持未提交，直至完整评审与新鲜验证通过后由工作区控制器做唯一一次已接受结果提交。
- 对外可观察行为一字不变：帧字节与时序均不变。
- 既有测试语义不得修改，只允许因类型或依赖变化做机械调整。
- 存储实现不得自建任何表。
- 开发阶段的策略是重建而非迁移：不得为既有数据编写回填、修复或迁移路径。
- 不得改动 `Tool/` 与 `.worktrees/legacy-reference/`；客户端可执行文件与 DLL 不得手工编辑。
- C# 文件为 CRLF、无 BOM UTF-8；`net10.0`，`TreatWarningsAsErrors`。每次以工具写入 `.cs` 后须做一次行尾归一化。

### 验证环境

- `dotnet test Server/86JP.Server.slnx -m:1`
- `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`
- 基线：944 通过 / 2 跳过 / 0 失败。`ChannelHostStartupTests` 在负载下会假红，复跑确认。
## Execution Result 1

### 本单元的交付已由 TASK-04 的 Contract Amendment A2 一并完成

TASK-04 移除 `ICharacterEntryMutationRepository` 时，协议层 `SelectCharacterPhaseCommands.cs:122` 对 `PersistPositionAsync` 的调用随之无法编译。留到本单元再改，TASK-04 交付的就是一棵编译不过的树；因此 TASK-04 以 A2 申报并改动了该处，把注入由 `ICharacterEntryMutationRepository` 换成 `ISelectedSessionPositionPersistence`。

这不是把本单元的责任提前吃掉：A2 明写「只改该文件里那一处依赖与调用，不动其余任何阶段的时序与语义」，本单元的职责相应落在**核验**上——协议层对人物持久化契约确已零命中、没有别的残留、对外行为未变。

### 核验

| ID | 命令 | 结果 |
|---|---|---|
| CK-014 | `grep -rn "ICharacterEntryMutationRepository" Server/src/Dfo.Server.Protocol --include=*.cs` | **零命中** |
| CK-015 | `dotnet test Server/tests/Dfo.Server.Protocol.Tests -m:1` | `已通过! - 失败: 0，通过: 200` |

扩大范围的补充核验（本单元自加，非契约要求）：协议层是否还直接持有**任何**人物持久化契约——

`grep -rnE "ICharacterEntryStateRepository|ICharacterStore|ICharacterRepository" Server/src/Dfo.Server.Protocol --include=*.cs` → **零命中**。

即协议层不再直接持有人物侧的任何仓储或存储，只经业务层入口与接缝。

### 现在留在协议层的那个依赖是什么

`PrepareSelectedCharacterSessionCommandHandler` 现在注入 `ISelectedSessionPositionPersistence`。它是 `Abstractions/Sessions/` 下的窄接缝，其实现在 TASK-04 中已由 Infrastructure 移至 Application。协议层因此依赖的是一个**业务层入口**，不是持久化实现——这正是 plan `### T-005` 的 Outputs「协议层依赖收敛」所要求的。

`AGENTS.md` 的「handler 业务语义规则」允许 handler 出现四类东西，其中第四类是命令派发与发送。这里的调用发生在**命令处理器**内而非 handler 内，不受该条约束；且该处理器本就是 SelectCharacter 多阶段工作流的阶段命令之一。

## Executor Self-check 1

| 验收条件 | 证据 |
|---|---|
| 1 协议层不出现人物的持久化契约 | CK-014 零命中，加上自加的扩大核验同样零命中 |
| 2 协议用例全部通过，既有用例只作因依赖变化的机械调整 | CK-015 200 通过；本单元自身零改动，TASK-04 在该文件上的改动为 **2 行**（构造器参数 1 行、调用点 1 行），无 using 变更——`using Dfo.Server.Abstractions.Sessions;` 在基线中已存在；先前记的「4 行」是 diffstat 的增删计数，已按复评 NB-1 更正。无断言改动 |
| 3 对外可观察行为不变 | 阶段时序、命令派发次序、`PersistsPosition` 判定条件均未改；仅被调用的接缝类型不同 |

### 自认的薄弱处

- 本单元没有自己的差异。这在流程上是可疑的形状，故如实写明其成因（A2）与本单元实际承担的职责（核验）。若评审认为责任转移应以 `task-superseded` 而非 A2 加核验来表达，这是要重议的地方。我的判断是：A2 已把责任转移写在明处，且本单元的三条验收条件都由可复核的证据满足，无需再造一个替代单元。
- CK-014 的过滤器只针对被移除的那一个契约名。真正想问的是「协议层是否还直接持有人物持久化」，故自加了更宽的核验，其结论比契约要求更强。

## Independent Quick Review 1

- 轮次标识：QR-05-1（本 Task 文件的首轮独立快评）
- 判定：**passed**
- 评审的契约版本：Contract revision 1，本 Task 文件无自身修正条款；连带核对 TASK-04 的 `Contract Amendment A2`（授权本单元交付物的实际改动者）
- 本单元自有差异身份：**空**。核验对象为未提交工作树中 `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs` 的 2 行改动（`git diff` 该文件 diffstat 为 `4 ++--`），由 TASK-04 的 A2 交付
- 按指示未复跑任何测试；CK-015 的结果按执行者记录采信

### 检视的命令与证据

| 项 | 内容 |
|---|---|
| 复跑的 grep | `grep -rn "ICharacterEntryMutationRepository" Server/src/Dfo.Server.Protocol --include=*.cs` → 零命中；`grep -rnE "ICharacterEntryStateRepository\|ICharacterStore\|ICharacterRepository" Server/src/Dfo.Server.Protocol --include=*.cs` → 零命中；再扩到全仓 `grep -rn "ICharacterEntryMutationRepository\|PersistPositionAsync" Server --include=*.cs` → 零命中 |
| 差异 | `git status --porcelain`；`git diff --stat -- Server/src/Dfo.Server.Protocol`；`git diff` 该文件与 `ProtocolModule.cs`；`git diff -- Server/tests/Dfo.Server.Protocol.Tests` |
| 读过的代码 | `SelectCharacterPhaseCommands.cs:1-145`、`ProtocolModule.cs:110-140`、`Abstractions/Sessions/ISessionPresence.cs:95-102`、`Application/Characters/CharacterStateAccess.cs:95-135`、`Application/ApplicationModule.cs:55-85`、`Infrastructure/Characters/SqliteCharacterStore.cs:110-190`、`git show HEAD:...SqliteCharacterEntryMutationRepository.cs`、`git show HEAD:...InfrastructureModule.cs` |
| 读过的文档 | 本 Task 文件全文、TASK-04 的 A1/A2/A3、`plan.md` 的 `### T-005` 与 BD-008/BD-012、`spec.md` 的 RQ-010/AC-016/EV-007、`AGENTS.md:105-125` 与 `:155-220` |

### 逐项核验

1. **责任转移的表达方式（执行者自己点名的重议处）——判定为可接受，不要求改用 superseded。** 本单元三条验收条件全是状态式的（零命中、用例通过、行为不变），契约并未要求本单元自产差异；A2 在动手之前申报，其编译序理由可独立复核为真（接口成员与 `SqliteCharacterEntryMutationRepository.cs` 整体已删，调用点必然编译不过）；共同约束禁止任务级提交，交付载体本就是同一棵未提交的树，逐 hunk 归属属记账层面而非交付层面。另按快评的授权边界，评审者不得选择替代单元或变更任务运行态——superseded 与否是 `executing-tasks` 的控制器处置，故此处不以流程形状判 `changes_required`；若控制器出于记账需要另记一条处置，与本判定不冲突。
2. **A2 的限定被遵守。** 该文件的实际差异恰为两处：构造器参数 `ICharacterEntryMutationRepository characterMutations` → `ISelectedSessionPositionPersistence positions`（:38），调用点 `characterMutations.PersistPositionAsync(` → `positions.PersistAsync(`（:121）。实参列表、`PersistsPosition` 判定、前后语句顺序、异常处理块均逐字未动。BD-008 只圈定该文件，未见越界。
3. **验收条件 1 成立**：三次 grep（含比契约更宽的两次）在协议层与全仓均零命中。
4. **验收条件 2 成立**：协议测试仅一处机械调整（`SelectCharacterPayloadCodecTests.cs` 反射构造 `SqliteCharacterEntryStateRepository` 时补入 `GoldLimitRuleService`，属 TASK-03/04 的连带），无断言改动。
5. **验收条件 3 成立，并已超出差异层面核到失败语义**：新接缝 `SelectedSessionPositionPersistence.PersistAsync` → `ICharacterStore.WritePosition` → `SqliteCharacterStore.FillPositionAsync` 使用与 HEAD 版 `PersistPositionAsync` **逐字相同**的 UPDATE 语句，且在受影响行数 `!= 1` 时抛出**同一条** `InvalidDataException("Entry position owner is unavailable.")`（另多一道 `RefuseUnlessOwnedAsync` 前置校验，同型异常）。写入仍在该调用内 await 完成（暂存后由接缝自开的 `DomainTransaction` 当场提交），未被推迟；`SelectCharacterHandler` 的 catch 不按异常类型分流（只有 `SelectCharacterPhaseFailure` 与 `Exception`），故失败路径的对外表现不变。`PrepareSelectedCharacterSessionCommand` 仍注册为 `Workflow`（`ProtocolModule.cs:112-114`），接缝自开边界不会与环境事务嵌套。
6. **协议层注入的确是业务层入口，非持久化实现。** `ISelectedSessionPositionPersistence` 声明在 `Abstractions/Sessions/ISessionPresence.cs:95`，唯一实现 `Application/Characters/CharacterStateAccess.cs:108`（`internal sealed`），注册在 `ApplicationModule.cs:71`；Infrastructure 侧的 `SqliteSelectedSessionPositionPersistence.cs` 已删除。附带确认可解析性未收窄：HEAD 时该契约只由 InfrastructureModule 注册，现由 ApplicationModule 以可选 `ICharacterStore` 无条件注册，无持久化的宿主由「启动即失败」变为「真要存位置时才响亮失败」，对含 Infrastructure 的真实宿主完全一致，不构成启动回归。
7. **`AGENTS.md` 的 handler 业务语义规则与该调用相容，执行者的辩解成立。** 那四类限制的主语是**消息 handler**（`AGENTS.md:186-200`，位于「Socket 层：handler 驱动领域命令」一节），同一节还明写「这个例外只授权生命周期协调，不授权 handler 直接修改领域状态；所有准备、补货、发布、PVP、任务、宠物与补偿动作仍分别由**阶段命令处理器**完成」（:212-214），目录表亦区分「消息 handler」与「命令 + 命令 handler」（:173-174）。`PrepareSelectedCharacterSessionCommandHandler` 属后者，正是领域动作该待的地方。另一条「禁止命令处理器嵌套 dispatch」也未触犯：该调用走的是领域接缝而非 `ICommandBus`。

### 阻断发现

无。

### 非阻断发现

| ID | 位置 | 内容 |
|---|---|---|
| NB-1 | 本文件 `Executor Self-check 1` 验收条件 2 一行 | 「TASK-04 在该文件上的改动为 4 行（1 行 using + 3 行调用点）」与实际差异不符：实为 **2 行**（构造器参数 1、调用点 1），**无 using 变更**——`using Dfo.Server.Abstractions.Sessions;` 在 `git show HEAD` 的同一文件里已存在；「4」只是 diffstat 的增删行计数。方向上不影响任何结论（真实差异比记述更小更保守），但本单元的交付物就是这份证词，建议接受时把该括号更正为「2 行：构造器参数 1、调用点 1；无 using 变更」。 |
| NB-2 | `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 本次工作树里协议层还有第二处改动（两条阶段命令 `Workflow` → `Transactional`），由 TASK-04 的 A3 明示申报，不属 BD-008 也不属本单元，无归属缺口；但本单元「协议层依赖收敛」的核验只覆盖 BD-008 那一个文件，未提及它。协议层整体的行为等价性留给最终评审一并核对。 |
| NB-3 | `Server/tests/Dfo.Server.Protocol.Tests` | CK-015 的 200 条用例中没有任何一条触及被替换的接缝（对 `ISelectedSessionPositionPersistence` / `PersistPositionAsync` 零引用）。故「协议用例全部通过」对本次替换而言是回归保护而非覆盖；接缝等价性的实际证据来自 TASK-04 的全量绿与上面第 5 条的代码核对。非本单元契约所要求，仅记录。 |
| NB-4 | `Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs:95` | 协议层现在注入的契约，名字与语义仍指向「持久化」，只是实现搬到了 Application 并自开事务边界。按 RQ-010 / AC-016 与 plan `### T-005` 的「业务层入口」判定为满足；若最终评审对 RQ-010 取更严读法（协议层不应直接触发人物状态落库，而应经会话/工作流），这是要重议的点，超出快评范围。 |

### 说明

`passed` 仅表示本 Task Unit 可以走执行者的接受流程，不构成最终质量批准。NB-1 建议在接受前顺手更正记录，无需另起评审轮次。

## Remediation 1 (post-verdict)

复评判 `passed`，无阻断。四项非阻断处置如下。

| findings | 处置 |
|---|---|
| NB-1 自检称「4 行（1 行 using + 3 行调用点）」与差异不符，实为 2 行且无 using 变更 | 已就地更正。这是本单元交付物的核心证词，数字不实即证词不实，方向虽更保守也不该留着 |
| NB-2 协议层还有第二处改动 `ProtocolModule.cs`（A3 的执行方式变更），不在本单元核验叙述中 | 补记：该处由 TASK-04 的 A3 明示申报，归属无缺口。本单元的核验对象是「协议层是否还直接持有人物持久化契约」，执行方式标记不属该问题，故不纳入；协议层整体行为等价性由 TASK-07 与最终评审承担 |
| NB-3 CK-015 的 200 条无一触及被替换的接缝，对本次替换是回归保护而非覆盖 | 属实。等价性证据来自复评的逐字比对（新接缝最终执行的 UPDATE 语句与基线 `PersistPositionAsync` 逐字相同、异常文本相同）与 TASK-04 的全量绿，而非 CK-015 |
| NB-4 接缝名字与语义仍指向「持久化」，只是实现搬到 Application | 记录。按 RQ-010 与 plan `### T-005` 的「协议层依赖收敛」判为满足；若最终评审对 RQ-010 取更严读法（协议层不得依赖任何以持久化命名的接缝），这是重议点 |

复评另独立核到一条本单元未主张的等价性证据：新路径最终执行的 UPDATE 语句与基线逐字相同，且在受影响行数不为 1 时抛出同一条 `InvalidDataException("Entry position owner is unavailable.")`。
