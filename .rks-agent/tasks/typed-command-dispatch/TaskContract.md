---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: Typed Command Dispatch

## Context

### Source Request

用户在审阅 `CommandBus` 时发现 `object?` 接缝与 `is TResult` 回收检查，追查后判断命令侧的枚举注册是从消息侧误迁移过来的设计，并授权重构：「可以，就这样重构，要清理掉is tresult，命令的枚举和检查」。随后补充：「别忘了走sdd这个重构还是有点危险的」。

在此之前用户已就形态作出决定：命令必须自带结果类型；handler 在泛型参数里声明自己处理哪个命令；注册时三者必须匹配才能通过；去掉枚举。经原型编译验证，采用 `AddCommandHandler<TCommand, TResult, THandler>` 三参数形态，约束 `TCommand : ICommand<TResult>` 与 `THandler : ICommandHandler<TCommand, TResult>`。

### Current Situation

命令派发以 `ApplicationCommandType` 枚举为连接键。同一个「哪个 handler 处理哪个命令」的事实被声明三遍——命令的 `CommandType` 属性、handler 的泛型基类、注册调用的枚举实参——三者之间没有任何编译期校验，注册错配要等到派发时由 `CanHandle` 发现。

管线契约为 `ValueTask<object>`（本分支上一次提交已将 `object?` 收紧为 `object` 并给 `TResult` 加了 `notnull` 约束），因此 `CommandBus` 末尾需要 `result is TResult` 把类型换回来，失败则抛错。

结构上这套机制是从消息派发照搬的：`MessageHandlerRegistry` 与 `CommandHandlerRegistry` 逐行同构，`ClientMessageHandler<TMessage>` 与 `CommandHandler<TCommand,TResult>` 的 `CanHandle` 与类型不符抛错措辞一致。消息侧的枚举来自协议——`NetworkPlugins.cs` 从帧的 `MessageNumber` 强转出 `ClientMessageType`，动态查表无可避免；命令侧没有这一跳，命令由代码构造，调用点静态已知类型。全解决方案检索确认 `ApplicationCommandType` 在 Networking 中零引用，仅用于注册表键、命令自报与日志。

现有 8 个命令、8 个 handler、4 个管线行为，全部命令为 `sealed record`，结果类型全部为非空引用类型。解决方案共 203 项测试通过。

## Goal

命令、结果与 handler 三者的对应关系由类型系统在注册点保证，派发链路全程携带静态结果类型；因枚举连接键与 `object` 接缝而存在的运行时弥合检查随之消失，且派发的可观察执行语义不变。

## Scope

### In

- 移除 `ApplicationCommandType` 及命令自报枚举的成员。
- 令注册点同时约束命令、结果与 handler 三者，错配无法通过编译。
- 使派发链路自终端 handler 至 `SendAsync` 返回值全程为静态结果类型，不经由 `object`。
- 删除仅为弥合上述两处动态性而存在的检查：handler 的 `CanHandle`、命令 CLR 类型不符抛错、`CommandBus` 的结果类型回收判定。
- 注册表连接键改为命令类型；保留重复注册检查，移除「枚举值必须全部注册」检查。
- 更新执行上下文中的命令标识，使日志仍能标明是哪个命令。
- 更新受影响的测试与测试替身。

### Out

- 消息派发侧的任何改动。消息枚举由协议决定，其设计在此不受质疑。
- 命令的业务语义、执行种类与 access mode 的取值。
- 命令、handler 的拆分、合并或重命名。
- 新增命令或 handler。
- `Tool/PvfLib` 与本分支上的 PVF 内容基础设施。

## Constraints

- 派发的可观察执行语义必须保持不变：管线行为的组成与相对顺序、online-user 与 account 闸门的获取与释放、嵌套派发拒绝、每次派发一个新作用域、失败与取消时的租约释放。
- 不得以放宽类型或抑制诊断的方式换取编译通过。
- 既有 203 项测试须继续通过；仅当某测试断言的行为本身被本任务有意移除时方可修改其断言，且须在结果中说明。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 解决方案中不存在 `ApplicationCommandType`，命令不再自报枚举 | 全仓检索结果为空 |
| SC-2 | 注册点若把 handler、命令或结果三者中任一配错，编译失败 | 三种错配各一个的编译探针，均产生编译错误 |
| SC-3 | 派发链路上不存在以 `object` 承载命令结果的契约，`CommandBus` 不再做结果类型回收判定 | 契约与 `CommandBus` 的代码审阅，加上全仓检索无残留 |
| SC-4 | `CanHandle` 与命令 CLR 类型不符抛错不再存在 | 全仓检索结果为空 |
| SC-5 | 未注册的命令被派发时显式失败并指明是哪个命令 | 针对未注册命令的测试 |
| SC-6 | 派发的可观察执行语义未变 | 覆盖行为顺序、两种 access mode 的闸门获取与释放、嵌套派发拒绝、按次新作用域的测试全部通过 |
| SC-7 | 既有测试不回归 | 全量测试运行结果，含对任何断言改动的说明 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「可以，就这样重构，要清理掉is tresult，命令的枚举和检查」，及随后的「别忘了走sdd这个重构还是有点危险的。如有必要允许使用subagent」 | 在当前 `codex/pvf-content-infrastructure` 工作区内重构命令派发的类型契约与注册方式，含相应测试改动与一次已验证的本地结果提交。 |

## Requires Confirmation

- 合并、推送、发起 PR，或对其他分支的写入。
- 消息派发侧的同类改造。

## Assumptions

- 命令均为 `sealed record`，故注册表按精确命令类型建键后，派发时解析到的 handler 必定匹配；若将来出现可继承的命令类型，此前提需重新评估。
- 所有 handler 注册均在 `Dfo.Server.Application` 程序集内完成，故适配类型可保持 internal。
- 本任务建立在同分支上一次提交所加的 `TResult : notnull` 约束之上。
