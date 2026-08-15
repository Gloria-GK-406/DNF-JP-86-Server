---
kind: rks-review
decision: approved-with-findings
blocking_findings: 0
---

# 变更复核：投递计划归类修正与接缝收紧（spec-005）

- 工作树：`D:\project\DNF\DNF-JP-86-Server\.worktrees\protocol-vertical-slices`
- 基线：`3c82821`（HEAD 仍为它；本次改动全部未提交）
- 复核方式：只读。未修改任何产品或测试代码。
- 判定：**approved-with-findings**，阻断项 0。

变更面（`git diff --stat 3c82821 -- Server/`）：3 个生产文件、7 个测试文件，`+78 / -37`。
无 csproj 改动，无 `Dfo.Server.Networking/` 改动，无 `Dfo.Server.Networking.Tests/` 改动。

---

## 逐条核对表

### 1. 投递语义是否逐字未变 —— 通过（最强证据：零差异）

```
$ git diff 3c82821 --stat -- Server/src/Dfo.Server.Networking/
(空输出)
```

`Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs` 相对基线**整文件零差异**，因而 `SendProjectionAsync`、`DeliverProjectionAsync`、`SendFrameAsync`、`ValidateProjectionDelivery` 四个方法体逐字未变——这比逐行比对更强，无需逐行比对即可结论。

为确认这四点语义确实存在于当前代码（而非"未变但也不存在"），我通读了该文件并逐项定位：

| 语义 | 位置 | 现状 |
|---|---|---|
| 必送批次先于尽力批次 | `DeliverProjectionAsync` 第 70-73 行 `foreach` 在第 75 行 `foreach` 之前，且 `await` 串行 | 在 |
| 与必送连接相同的目标被跳过 | 第 77-78 行 `if (target.ConnectionId == requiredDirectConnectionId) continue;` | 在 |
| 尽力目标失败时 `break` 当前目标、不影响后继目标 | 第 90-95 行 `catch { break; }` 位于**内层** frame 循环，外层 target 循环继续 | 在 |
| 取消异常照原样上抛 | 第 86-89 行 `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }`，且置于通用 `catch` **之前** | 在 |
| 空值校验及其异常类型 | `ValidateProjectionDelivery` 第 117-138 行全部为 `InvalidDataException`；`DeliverProjectionAsync` 第 65-67 行三条 `ArgumentNullException.ThrowIfNull` | 在，类型未变 |

结论：投递次序、失败隔离、取消传播、去重跳过、空值校验异常类型五项**全部逐字未变**。CSC-5 / CSC-6 / RQ-006 / RQ-007 满足。

### 2. 测试替身的断言强度是否下降 —— 通过（7 处逐一核对，无一削弱）

**2a. `SelectCharacterProjectionContractTests.RecordingSender`**

用 `git show 3c82821:...` 取基线全文与当前全文逐行比对：

- 测试方法体第 25-29 行五条断言**逐字未变**：

```csharp
Assert.Equal(owner, sender.ConnectionId);
Assert.Same(direct, Assert.Single(sender.Delivery!.RequiredDirectProjections));
var addressed = Assert.Single(sender.Delivery.BestEffortTargets);
Assert.Equal(target, addressed.ConnectionId);
Assert.Same(publication, Assert.Single(addressed.OrderedProjections));
```

- 第 18 行 `await ((INetworkMessageSender)sender).SendProjectionAsync(` 强制转换保留，Arrange 段（第 12-23 行）逐字未变。
- 替身内部：基线 `SendAsync` 记录并 `Assert.IsType<SelectCharacterProjectionDelivery>(message)`；现 `SendProjectionAsync` 直接记录，`SendAsync` 改为 `throw new InvalidOperationException("Projection delivery must not travel through the message send path.")`。

**新写法严格不弱于旧写法**，理由是两条独立的：其一，旧的 `Assert.IsType<SelectCharacterProjectionDelivery>` 所证之事（"到达 sender 的对象是投递计划"）在新写法中由 `SendProjectionAsync` 的**形参静态类型**保证，而形参类型是编译期事实，强于运行期断言；其二，新增的 `SendAsync` 抛出把"投递不经消息发送路径"从**未被断言**升级为**被断言**——旧写法恰恰是走那条路径的，根本无法表达这一点。这是净增强，不是等价替换。

**2b. 另 6 处替身**

| 文件 | 替身 | 新增实现 | 判定 |
|---|---|---|---|
| `Channel/ChannelConnectHandlerTests.cs:49` | `RecordingSender` | throw | 恰当 |
| `Channel/ChannelControlMessageHandlerTests.cs:107` | `RecordingSender` | throw | 恰当 |
| `Game/GameExitMessageHandlerTests.cs:61` | `RecordingSender` | throw | 恰当 |
| `Game/GameLoginTests.cs:130` | `RecordingSender` | throw | 恰当 |
| `OnlineUsers/OnlineUserServiceTests.cs:459` | `RecordingInitialSender` | throw | 恰当 |
| `Sessions/SessionGenerationOwnershipTests.cs:194` | `NoopSender` | 空实现 | 恰当 |

这 6 处**均未触碰任何既有断言、既有 `SendAsync` 实现或任何测试方法体**——diff 全部是纯新增方法（每处 +6 或 +3 行，无删除行）。因此不存在断言被削弱的可能性。

**"抛出是否会掩盖或改变某条既有测试原本覆盖的路径" —— 独立确认（不依赖全绿）**

我用静态可达性证明这 5 处 throw 在测试运行中**不可能被触发**，两步：

第一步，`SendProjectionAsync` 在整个 `Server/src` 中的调用点只有两处宿主：

```
$ grep -rn 'SendProjectionAsync' Server/src --include=*.cs
Application/Commands/Game/SelectCharacterCommand.cs:127,198,207,219,225,229,239,248,253
Application/Sessions/SelectCharacterLifecycleServices.cs:67
（另加 Abstractions 的声明与 Networking 的实现）
```

即：一个替身的 `SendProjectionAsync` 要被执行，该替身必须被注入到 `SelectCharacterCommandHandler` 或 `SelectedSessionCleanupService`。

第二步，检索这 5 个测试文件是否构造上述两个类型：

```
$ grep -n 'SelectCharacterCommandHandler\|SelectedSessionCleanupService\|SelectCharacterCommand' <5 个文件>
ChannelConnectHandlerTests.cs        → none
ChannelControlMessageHandlerTests.cs → none
GameExitMessageHandlerTests.cs       → none
GameLoginTests.cs                    → none
OnlineUserServiceTests.cs            → 仅 ISelectedSessionCleanupService 的桩实现 NoopCleanup / ClearingCleanup（第 509、515 行），而非真实 SelectedSessionCleanupService
```

前四者的替身分别注入 `ChannelConnectHandler` / `ChannelControlMessageHandlers` / `GameExitHandler` / `GameLoginHandler`，这四个类均不在 `SendProjectionAsync` 调用点列表中；`OnlineUserServiceTests` 的替身注入 `OnlineUserConnectionObserver`，其 `cleanup` 依赖由桩件提供，真实清理服务从未被构造。故五处 throw **静态不可达**，不掩盖也不改变任何既有覆盖路径。此结论独立于测试全绿。

`NoopSender`（`SessionGenerationOwnershipTests`）另作分析：它确实被注入真实的 `SelectedSessionCleanupService`（第 58、127 行），而该服务在 `SelectCharacterLifecycleServices.cs:67` 会调用 `SendProjectionAsync`——但仅在 `targets.Count > 0 || required.Count > 0` 时；这两处用例传入空的 `new SessionPresenceRegistry()`，`currentPresence` 为 null，故两个集合均为空，不触发调用。**且即便触发也安全**：基线的默认实现落到 `NoopSender.SendAsync`（返回 `ValueTask.CompletedTask` 的空操作），新的空实现同样是空操作，行为完全等价。此处选空实现而非 throw 是正确判断——若选 throw，且未来触发，会被该服务第 68 行的 `catch (Exception error) { failures.Add(error); }` 吞进 `AggregateException`。

结论：7 处替身调整，**无一削弱断言**，1 处（`RecordingSender`）净增强。CSC-8 / RQ-009 / AC-009 满足。

### 3. `SendAsync` 是否真的不再接受投递计划 —— 通过（编译期）

`Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs:30-32`：

```csharp
public sealed record SelectCharacterProjectionDelivery(
    IReadOnlyList<Dfo.Server.Data.Game.ISelectCharacterSupplementalProjection> RequiredDirectProjections,
    IReadOnlyList<ExplicitProjectionTarget> BestEffortTargets);
```

无基类列表，无 `MessageType` 成员。

```
$ grep -n 'IServerNetworkMessage\|MessageType' Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs
26:/// implement <see cref="IServerNetworkMessage"/>: ...   ← 注释
38:        IServerNetworkMessage message,                    ← SendAsync 形参
```

投递计划定义处零命中，与 CK-005 期望一致。`SendAsync` 第二形参静态类型为 `IServerNetworkMessage`，而 `SelectCharacterProjectionDelivery` 不实现它、且是 `sealed record`（无隐式转换、无用户定义转换），故传入必然产生 CS1503 参数类型不匹配。**这是编译期不成立，不是运行期失败**。

同时 `INetworkMessageSender.SendProjectionAsync`（第 41-44 行）已改为以分号结尾的普通接口成员，无方法体、无回退。CSC-3 / RQ-003 / RQ-004 / AC-003 / AC-004 满足。

### 4. 枚举独占 —— 通过

```
$ grep -rn 'ServerMessageType.GameSelectCharacter' Server/src --include=*.cs
Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs:150:    public ServerMessageType MessageType => ServerMessageType.GameSelectCharacter;
```

**恰好一处**，属 `SelectCharacterResponse`（第 148-151 行）。`Server/tests` 下零命中。基线时该值被 `SelectCharacterResponse`（第 150 行）与 `SelectCharacterProjectionDelivery`（第 165 行）两个 CLR 类型共用，现已消除。CSC-4 / RQ-005 / AC-005 满足。

### 5. 引用图未放宽 —— 通过

全部 `Server/src/*/*.csproj` 的 `ProjectReference`（且本次**无任何 csproj 被修改**，见 `git status`）：

```
Dfo.Server.Abstractions   → Dfo.Server.Data                                   （仅此一条）
Dfo.Server.Networking     → Dfo.Server.Abstractions, Dfo.Server.Data          （无 Application，无 Protocol）
Dfo.Server.Application    → Dfo.Server.Abstractions, Dfo.Server.Data
Dfo.Server.Protocol       → Dfo.Server.Abstractions, Dfo.Server.Data, Dfo.Server.Networking
Dfo.Server.Data           → （无）
```

迁入的两个记录所需类型均在既有可达范围内、且**在基线时就已被同一文件使用**：`NetworkConnectionId`（`Dfo.Server.Data.Networking`）由第 1 行既有 `using Dfo.Server.Data.Networking;` 提供，`ISelectCharacterSupplementalProjection`（`Dfo.Server.Data.Game`）在基线的 `ISelectCharacterProjectionComposer.Compose`（第 55-56 行）签名中已以同样的完全限定写法出现。**未引入任何新引用方向**。

方向正确性另证：`Dfo.Server.Data` 无任何项目引用，故 Data 不可能反向依赖 Abstractions；`grep -rn 'SelectCharacterProjectionDelivery\|ExplicitProjectionTarget' Server/src/Dfo.Server.Data` 零命中，Data 中确无遗留使用者。CSC-7 / RQ-008 / AC-008 满足。

### 6. 是否有遗漏的实现者 —— 通过（10 个实现者，逐一配平）

全仓 `INetworkMessageSender` 实现者与其 `SendProjectionAsync` 实现位置一一对应：

| # | 实现者 | 声明处 | `SendProjectionAsync` |
|---|---|---|---|
| 1 | `TouchSocketNetworkMessageSender`（生产，唯一） | `Networking/Runtime/...:11` | `:47` 显式（基线即有，本次未动） |
| 2 | `ChannelConnectHandlerTests.RecordingSender` | `:33` | `:49` 新增 |
| 3 | `ChannelControlMessageHandlerTests.RecordingSender` | `:91` | `:107` 新增 |
| 4 | `GameExitMessageHandlerTests.RecordingSender` | `:45` | `:61` 新增 |
| 5 | `GameLoginTests.RecordingSender` | `:114` | `:130` 新增 |
| 6 | `SelectCharacterLifecycleTests` 的替身 | `:691` | `:728` 基线即有 |
| 7 | `SelectCharacterProjectionContractTests.RecordingSender` | `:32` | `:42` 新增 |
| 8 | `OnlineUserServiceTests.RecordingInitialSender` | `:443` | `:459` 新增 |
| 9 | `SessionGenerationOwnershipTests.NoopSender` | `:190` | `:194` 新增 |
| 10 | `SessionGenerationOwnershipTests.RecordingSender` | `:199` | `:204` 基线即有 |

10 个实现者，10 处实现，无缺口。`Networking.Tests/Integration/OutboundSendFailureTests.cs:109` 是从 DI 取 `INetworkMessageSender`（拿到的即生产实现），非新实现者。

"是否有实现者靠编译器没报错蒙混过去"——**不可能**：`SendProjectionAsync` 现为无默认实现的接口成员，任何未实现者是 CS0535 编译错误而非警告，`TreatWarningsAsErrors` 与否都拦得住。本次构建 0 错误即证明全部配平。CSC-8 满足。

### 7. `NetworkMessages.cs` 是否只删了该删的 —— 通过

`git diff 3c82821 -- Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs` 为**单一连续删除块，`-16 / +0`**：恰为第 153-167 行的 `ExplicitProjectionTarget`、`SelectCharacterProjectionDelivery` 两个记录及其上方的 `<summary>` 注释，加尾随空行。无其他增删、无重排、无格式变动。上下文 `SelectCharacterResponse`（第 147-151 行）与 `GameExitResponse` 完整保留。BD-001 精确落地。

两个记录的注释在迁入后**原文保留**，另新增一段 `<remarks>`（`INetworkServices.cs:22-29`）说明"这是投递计划不是消息"。属信息净增。

### 8. `ServerMessageEncoding.cs` 注释改写是否准确 —— 通过

改写为：

> Encoders are keyed by the message's CLR type rather than by its catalogue member. An outbound message is constructed by server code, so its type is statically known at the call site and no dynamic key is needed -- the same reasoning that keys commands by their own type.

删去的：

> And the catalogue could not serve as the key even if that were wanted: `SelectCharacterResponse` and `SelectCharacterProjectionDelivery` both report `ServerMessageType.GameSelectCharacter`, so one catalogue value covers two messages.

两项判断：

- **删去的部分确已不是事实。** 第 4 项检索确认 `ServerMessageType.GameSelectCharacter` 现仅被 `SelectCharacterResponse` 一个类型报告，"一个目录值覆盖两条消息"不再成立。继续保留会是一句假话，删除是必须的，不是可选的。
- **保留的理由仍然成立。** `TouchSocketNetworkMessageSender.SendAsync` 第 20 行 `encoders.Find(message.GetType())` 确实按 CLR 类型查找；出站消息全部由服务端代码 `new` 出具体类型后传入，调用点静态已知，无需动态键。与实现一致。

注意这里的语义变化：原文给了两条理由（"不需要动态键" + "即便想用目录键也做不到"），改写后只剩第一条。第一条是**独立充分**的设计理由，不依赖第二条；且第二条在本次改动后已成为伪命题。故改写既准确又不弱化论证。BD-003 落地正确。

### 9. 构建与全量测试 —— 通过，与基线完全对齐

**构建**（自仓库外 cwd，`-nodereuse:false`）：

```
$ cd /d/project/DNF && dotnet build DNF-JP-86-Server/.worktrees/protocol-vertical-slices/Server/86JP.Server.slnx -nodereuse:false
已成功生成。
    0 个警告
    0 个错误
已用时间 00:00:04.95
[exited with code 0]
```

15 个项目全部产出程序集。CK-001 满足。

**全量测试**（同 cwd 与开关，完整运行两次，两次均全绿；下为第二次的完整分项）：

```
失败: 0，通过:  51，已跳过:  9，总计:  60 - Dfo.Pvf.Tests.dll (net10.0)
失败: 0，通过: 256，已跳过: 10，总计: 266 - Dfo.Server.Application.Tests.dll (net10.0)
失败: 0，通过:  84，已跳过:  0，总计:  84 - Dfo.Server.Protocol.Tests.dll (net10.0)
失败: 0，通过:  45，已跳过:  0，总计:  45 - Dfo.Server.Networking.Tests.dll (net10.0)
失败: 0，通过:  75，已跳过: 46，总计: 121 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
失败: 0，通过:   4，已跳过:  0，总计:   4 - Dfo.Server.Host.Tests.dll (net10.0)
EXIT=0
```

合计：**总计 580，通过 515，跳过 65，失败 0**。分布 Pvf 60 / Application 266 / Networking 45 / Protocol 84 / Infrastructure 121 / Host 4，与基线**逐项一致**。

关于 `Dfo.Server.Host.Tests` 的瞬时失败：本次两轮运行均为 4/4 通过，**未复现**该已知瞬时失败。如实记录：本次复核期间未观察到此现象。

`Dfo.Server.Networking.Tests` 45 项全通过，且该目录相对基线**零差异**（`git diff --stat 3c82821 -- Server/tests/Dfo.Server.Networking.Tests/` 空输出）——即线级投影投递的字节断言原封未动仍全绿，CSC-6 / RQ-007 / AC-007 得到未经修改的测试证明。CK-002 / CK-008 / AC-010 满足。

---

## 契约与规格覆盖小结

| ID | 判定 | 依据 |
|---|---|---|
| CSC-1 / RQ-001 / AC-001 | 满足 | 两记录位于 `Dfo.Server.Abstractions.Networking`（`INetworkServices.cs:13-32`），Data 中零遗留 |
| CSC-2 / RQ-002 / AC-002 | 满足 | 无基类列表、无 `MessageType`（第 3 项） |
| CSC-3 / RQ-003 / RQ-004 / AC-003 / AC-004 | 满足 | 类型不匹配为编译期错误；接缝无方法体（第 3 项） |
| CSC-4 / RQ-005 / AC-005 | 满足 | 枚举成员恰一处报告者（第 4 项） |
| CSC-5 / RQ-006 / AC-006 | 满足 | 传输实现零差异 + 相关测试未削弱且全通过（第 1、2 项） |
| CSC-6 / RQ-007 / AC-007 | 满足 | Networking.Tests 零差异且 45/45 通过（第 9 项） |
| CSC-7 / RQ-008 / AC-008 | 满足 | csproj 未改动，引用图未放宽（第 5 项） |
| CSC-8 / RQ-009 / AC-009 / AC-010 | 满足 | 580 项对齐、0 失败；7 处替身调整已逐一说明（第 2、9 项） |

Scope/Out 未被侵犯：无协议语义、字节布局、加密参数改动；`ISelectCharacterSupplementalProjection` 未迁移未改写；`ServerMessageType` 成员与取值未动；未出现放宽类型、抑制诊断或关闭 `TreatWarningsAsErrors` 的痕迹（0 警告 0 错误，且无 `#pragma` / `<NoWarn>` 新增）。

---

## 发现清单

### F-1 · 非阻断 · 遗漏的过时注释

`Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs:42-46`：

```csharp
/// <remarks>
/// Implemented explicitly rather than left to the interface default. The default forwards to
/// <see cref="SendAsync"/>, which is why that method used to need a type test for the delivery
/// message; taking the method over removes the reason for the test.
/// </remarks>
```

这段 `<remarks>` 整体以"接口存在默认实现"为前提解释为何要显式实现。本次改动已删除该默认实现，这段话现在描述的是一个不存在的事实——与本次特意去修正的 `ServerMessageEncoding.cs` 过时注释属于**完全同一类的问题**，只是被漏掉了。它不影响任何行为，也不影响任何测试，故非阻断；但留着会误导下一个读者以为接口仍有回退路径，恰好抵消掉本成果想立起来的那条认知。

建议处置：删除该 `<remarks>`，或改写为一句陈述现状的说明（例如"投递计划不是消息，故没有可回退的默认实现；本类是该接缝的唯一生产实现"）。这属于本次改动范围内的收尾，不需要新开成果。

### F-2 · 非阻断 · 抛出型替身的未来可吞噬性（观察，非当前缺陷）

5 个新增的 throw 型 `SendProjectionAsync` 今天静态不可达（第 2 项已证）。但需记录一点前瞻：`SelectedSessionCleanupService`（`SelectCharacterLifecycleServices.cs:67-70`）把 `SendProjectionAsync` 包在 `try { ... } catch (Exception error) { failures.Add(error); }` 中。若将来某个用例把这类抛出型替身接入清理服务，替身的 `InvalidOperationException` 会被吞成 `failures` 中的一条、最终表现为 `AggregateException`，而不是干脆地把测试打红。今天无任何影响，且本次为 `NoopSender`（唯一真正接入该服务的替身）选择空实现而非抛出，恰恰避开了这个坑——判断是对的。

建议处置：无需改动。仅作为后续接入投影路径时的注意事项记录。

### F-3 · 非阻断 · 关于归类修正本身的架构判断（我的意见：认同）

我认为这次归类修正在架构上是**正确**的，不是"为改而改"。四条理由，其中三条是我独立核验过的项目事实而非转述：

1. **它确实没有线上表示。** `ServerMessageEncoderRegistry` 中无其注册项，`SendAsync` 第 20-22 行的 `encoders.Find(...) ?? throw` 意味着真走消息路径只会得到 "has no registered encoder" 的运行期异常。一个消息接口的实现者若在任何编码器注册表中都不存在，它就不是消息。
2. **它携带自己的收件人，且是复数。** `ExplicitProjectionTarget.ConnectionId` 与 `SendAsync(connectionId, ...)` 的单一收件人模型正面冲突——消息的收件人由 sender 的调用方给出，而这个类型自带一份扇出名单。这不是 DTO 的形状，是投递计划的形状。
3. **它与 `SelectCharacterResponse` 抢同一个目录值。** 这是 spec-002 中迫使注册表按 CLR 类型建键的那个冲突。归类修正之后目录恢复一值一义（第 4 项已验证）。
4. **落点选择也合理。** `Dfo.Server.Abstractions` 并非纯接口程序集——它已有大量伴随接口的具体记录（`AccountEntryStorageRecord`、`CharacterProfileStorageRecord`、`ChannelEntryOptions`、`CollectionBoxDefinition` 等数十个），把"接缝方法的唯一参数形态"放在声明该接缝的文件里，与项目既有惯例一致，不是新开的先例。

可能的反对意见我也考虑过并不予采纳：有人会主张"它装的是 `Data.Game` 的领域投影，所以该留在 Data"。但载荷的归属地不决定容器的归属地——它装的是投影**加上收件人加上必送/尽力的分级**，后两者是投递策略，而投递策略的唯一消费者是 `INetworkMessageSender` 这条接缝。留在 `Data.Networking` 才是错位：Data 中没有任何一处使用它（已检索确认零命中），它在那里纯属寄居。

同理，"它应当仍是消息"的主张也不成立——若坚持它是消息，就必须为一个"一次产生多个不同收件人的多个帧"的东西写编码器，而 `IServerMessageEncoder.Encode` 返回的帧列表全部发往同一个连接（`SendAsync` 第 34-39 行的循环用同一个 `connection`）。模型上装不下。

建议处置：无。此条仅为对"归类修正是否正确"这一提问的正面回答。

### F-4 · 非阻断 · 完全限定名的书写风格

`INetworkServices.cs` 第 16、31 行沿用 `Dfo.Server.Data.Game.ISelectCharacterSupplementalProjection` 的完全限定写法。这与同文件 `ISelectCharacterProjectionComposer`（第 56 行）一致，是有意为之的一致性选择，plan 第 44 行也明示了。不构成问题。若日后觉得碍眼，可在文件顶部补一条 `using Dfo.Server.Data.Game;` 并统一去掉限定——纯风格，与本次成果无关。

建议处置：无需改动。

---

## 结论

阻断级判据逐条落空：

- 投递次序或失败隔离语义的改变 —— **无**（传输实现整文件零差异）
- 被削弱的断言 —— **无**（5 条核心断言逐字保留，另一条被更强的编译期约束取代）
- 引用图放宽 —— **无**（csproj 未改动，无新引用方向）
- `SendAsync` 仍可接受投递计划 —— **否**（`IServerNetworkMessage` 实现已去除，传入为 CS1503）

改动是干净的：变更面精确等于契约描述的范围，没有夹带，没有为通过编译而做的让步，构建 0 警告 0 错误，全量测试 580 项与基线逐项对齐、0 失败。唯一实质意见是 F-1 的一处过时注释——它与本次改动特意去修正的那类注释是同一个问题，属遗漏而非分歧，改一行即可。

判定：**approved-with-findings**，阻断项 0。
