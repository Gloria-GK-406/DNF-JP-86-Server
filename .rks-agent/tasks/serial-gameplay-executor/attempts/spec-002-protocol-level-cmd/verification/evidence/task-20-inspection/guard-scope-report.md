# TASK-20 r1：实际跨 request guard 漏洞与最小 scope 方案

2026-10-25；产品 HEAD `d6708faf9b544662fc9d976dba369fbf410f719f`。这是局部 task-rework 证据，不是 Task Result、Spec terminal 或产品修正批准。根/子契约、B3/P4不改；产品仍逐 byte 等 HEAD，新 whole absent。

## 真实 RED

`task-20-guard-before-02` 的 runner/meta/完整 console 在 `verification/logs/`，2026-10-24 17:21:59–17:22:08 UTC，真实 dotnet exit1，两行为测试失败，不是编译/fixture/timeout。前一轮 01 因探针误用不存在的 GetAsync 编译失败，仅诊断，已修为实际 FindAsync；不能计行为 RED。

同 provider 已注册 MatrixProbe 先完成真正 owner request/SettledRequiredExplicit 登记，得到已完成 token；随后由同 ICommandBus 创建真正新 ProtocolEffectSelected request、新 job、新 ledger entry，在 handler 中捕获该旧 token 实际 adapter Enter guard。

- return case：捕获原 InvalidOperationException 后最终正常返回，current request Failure=null、Closed=true，entry Rejected；owner Closed=true/entry Committed，只有 owner.Failure 被标记。encoder总调用1，没有第二次编码，没有新增出站/source-close，没有围栏。
- write case：捕获后进入 ordinary WriteAsync，实际 ICharacterCarriedGoldTable.AddAsync(+37)，同事务 FindAsync staged100037；最终 durable100037（before100000）。request仍 Failure=null，最终正常返回；owner/caller entry分别 Committed/Rejected，无围栏/source-close。真实 SQL delegate1；非仅计数器证明。
- 两例均相同旧 token、不同 request/job/entry，完整 guard stack 直接落 `CommandRequestContext.DemandRegistrationCurrent → CommandRegistrationAttempt.Enter → OutboxNetworkMessageSender.RegisterExplicitAsync → same Registering wrapper → MatrixHandler`。当前新 caller 没有 incomplete attempt，原 owner 的 Failed 无法阻止 caller 的 DemandCurrent。

产品前两次 fresh capture／pure family 和所有原 bytes/SHA 已封存。新增行为探针仅修改白名单测试 `ExplicitEffectCommandTests.cs`；未改任何产品、旧 tests、ledger、Task前缀或 gate。当前 RED 应保留作后继修正同目标 GREEN 的起点。

## 最小 API／物理路径方案（待 Task r2 指定）

增加且仅增加一个生产物理路径：`Server/src/Dfo.Server.Abstractions/Execution/GameplayJob.cs`。复用已有 Active token 的 first ProgramError 槽；不增加字段、ambient、DI current request、request locator、job slot 或第二 Host。

建议具名窄 API：`public static void RecordProgramError(Exception error)`。验证 error 非 null；如果已有 Active token，调用现有 `token.Fault(error)`，保 token 已有 first-program-error/Interlocked first-wins；若没有 active job，完全不记录、不造另一个异常、不新入 job。调用方仍抛原始 guard 对象；API 本身不读取世界/请求、不提供授权、不等待/发送，不改变 Current/IsOnExecutor。

这解决真实 opaque 边界无法取得 caller 的问题：token只携 owner，任何由 token.request.Failed 进行的修改均只作用 owner；当前 adapter 与显式 request 两者都没有安全 current-caller request 参数。不能靠在现文件另存 request 或临时包装 Func 修复。

拟定调用点：

1. `CommandRegistrationAttempt` 的内部 Enter 校验 catch（覆盖 unknown token、wrong request/job、closed、phase/call/physical target、reuse 的真正 guard；实际 encoder span不在此catch）：用新 API 标记 **当前实际 caller job**，同时保真实 token owner 的原 Failed(error)，再原样 throw。失败记录同一原 guard，不伪造 GameplaySelfWaitException。
2. `CommandRequestContext.BeginRegistrationAttempt` 的本 request 许可/state/unit/生命周期 guard 用已有 Failed(error) 使本 request sticky；如要求 foreign/closed request 的公开 DemandCurrent catch 同样保护实际 caller，沿新 API 标记其 active job。保原 DemandCurrent 完整校验体、不公开更弱方法、不更改 authority/participant 或终判模型。详细范围由 r2 准确限定，不能以方法名称绕过故障来源窄 span。
3. actual encoder故障继续只用现 EncoderFailed(request) 来源，不给它新增 ProgramError 标签，避免破坏三个合法 raw 出口。callback/typed转换/currentlookup/Register 仍无 EncoderFailure章。普通无token重载、P3 commit-only、原两许可、outbox保序/source关闭、drain/Close/scope一次终判完全保持。

`GameplayJob.DemandHealthy` 和 executor 现有 end/program-error 检查会看到当前 caller first fault，使 catch 后 real WriteAsync 在 action前拒绝，正常 return 在 request.DemandFinished／executor收口失败；没有 current request通道。其 off-executor行为必须动态区分：新API不改变原 guard抛对象，不恢复closed owner；有active caller才记录它。已有first-error不得覆盖，新guard应保在owner/request原记录和实际throw，不默丢额外独立错误。

## 后继实际验证义务

同两行为目标先 GREEN：gold100000不变、SQL delegate0，current caller不能正常return，保原guard cause/两端Closed/owner Committed/原Ordinal／source-close/fence实际结果；不能以 ProgramError 静态源码冒通过。补本次 fresh live-owner/foreign **真正新显式请求** 的两个catch后write/return对照及ownerdrain；不新增第二 GameplayJobHost。Begin permission/unknown phase/state/open unit等捕获后实际SQL/return矩阵亦仍未跑，应在r2继续，不将当前两closedowner例外推全矩阵。

对 scope dispose/independent同对象 cause、原 raw encoder及默认失败保护必须复跑，确认first-error marker不抢先把正常传播变独立发生、不破坏 scope活着时drain和最后一次FailAsync。共享改动完整Release/八full/DFO/Counter等原r1义务不变。
