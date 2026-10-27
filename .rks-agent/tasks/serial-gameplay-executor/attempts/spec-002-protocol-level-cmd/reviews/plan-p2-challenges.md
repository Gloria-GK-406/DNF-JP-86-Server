# Plan P2 有界 adversarial challenges

## 目标、权威与攻击范围

- 角色：fresh independent generator `b_plan_p2_challenges`，非 producer、非 reviewer，未委派；使用 `generating-adversarial-challenges/SKILL.md`，本记录 immutable。
- 工作区：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；产品 HEAD `2d8081ae2a3ef687e88ec284df9e97588cc625a7` 由主控指定，本轮不运行 git。该工作区无 `.codegraph/`，故直接只读源码。
- 精确目标（均实际核对 SHA-256）：`../plan.md` P2 `0dd2dc61f5a6fa606e0fc308d2339cd578b7f543c87de9d55bc3ec9d29dc3212`；`../routing-decision.md` `54a0dcf923faa180316ddda5e31d18052c937f709c24a2cdac677c4ebb817f33`；`../verification/plan-p2-amendment.md` `14a1cb4dca0d0dda5c00b533d18403b1048d61c414ffe96285861f2eee11625e`。
- Governing：子 `../TaskContract.md` r1 Goal、SC-8B、SC-13；根 `../../../TaskContract.md` r4 及 `../../../spec.md` RQ-023(b)/(c)、RQ-024、RQ-031(c) 的行为／保护／事务等价；已独立 approved 的 `../spec.md` B2（实际核对 SHA-256 `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`），尤其 RQ-004～009、AC-002～005。已读 `spec-b2-review.md`、`spec-b2-challenges.md`、`effect-registration-boundary-review.md` 及 B2 amendment，作为批准边界与原 finding 输入；下面判断仍独立回到实际源码，不以旧结论代替攻击。
- 本轮仅攻击 P2 新共享决策 §3／4／5、T-004、对应 Verification／Next Planning Boundary 和四个 gate 路径 override：真实 encoder 出处、不可伪造且显式透传的许可、sticky 失败及最终收口、唯一 ledger／显式 replay、同容器测试路径。不重开 P1 其他路线，不动 sealed A 或 Task01～10，不要求未授权 Task11 的局部 API／实现成为高层 Plan gate 前提。

## 保留的挑战

本轮保留 **0 条**：high-risk 0，low-risk 0。此结果不是 review verdict、完整性证明或产品实现声明。

已尝试以下具体失败路径，未形成需要此时修改 Plan 的可信缺陷：

- **把业务／callback／登记守卫错误伪装成获许可的 encoder 故障。** 实际 `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs:61–79,88–153` 显示 EncodeBatch、required direct 的 EncodeEach 与 lookup、校验、缓冲区、Register 位置可分；best-effort 的 encoder 异常另有逐目标 catch。P2 §4 不把整个 EncodeEach 或登记 call 一概包入豁免：只有实际 encoder 调用的窄 catch 可内部记 cause，buffer／lookup／callback／Register 没有该证明。request 签发后经 Game typed adapter、Abstractions opaque 参数直达同程序集实际 adapter；sealed 内部实现、无公开自报入口，并同时绑定具名注册出口、阶段、job、request、精确物理目标及调用身份。跨请求／目标／closed／leaked 和可编译来源 mutant 的验证要求已写明。具体方法命名可留临近 Task，不构成当前缺陷。
- **先做不可撤销 typed 包装或 raw 裁决，再漏掉独立清理失败。** 当前 `Application/Commands/CommandBus.cs:90–109` 的两次 FailAsync、`Application/Transactions/CommandWorkUnits.cs:90–104` 的提前包装／围栏确是必须替换的接点；`Application/Commands/CommandRequestContext.cs:62–71` 的 Includes 会按引用及异常链去重。P2 明确要求先 sticky 失败、在 scope 活着时 drain 所有已启动单元，再 Close／dispose／收集独立发生，最后一次性分类；独立 unit／dispose 即使抛同一 Exception 对象也撤销 raw，不能用 Includes 或 ReferenceEquals 吞掉。原 typed 不确定事实保留优先级，普通 postcommit／未知／guard／双故障仍全参与者保护。`CommandWorkUnitsTests.cs:265–317` 的既有负对照在拟验中明确保持。未发现仅靠源对象相同就能获得 Plan 授权的路径。
- **catch 后恢复写权、重放创建新 ledger，或重试延长原窗口。** 实际 `Application/Effects/EffectOperationLedger.cs:18–74` 只有原物理连接 CWT；LatestOrdinal 推进与按原 entry.Ordinal 淘汰先于重放，ConfirmedRollback 只重置 state／projection，不替换 ordinal。P2 §3 抽纯 admission 并保该唯一身份，明确不调用旧 Dispatch／ambient 来执行新 CMD，Pending 结束拒绝及原 raw 身份冲突保持；§4 要求登记失败立即 sticky，catch 不能再结算／写入或成功返回。其验证同时覆盖连续 singleton 请求和结束续体。没有允许新增账本或重试续窗的共享决策。
- **reader 内部仍暗读旧请求，或把旧 replay 写事务误改只读／nested bus。** 实际 `Application/Effects/ReplayStateDomainService.cs:37–46` 内部 Capture(session)，以及 `Application/Commands/SelectedSessionSafety.cs:29–33` 的 ambient metadata 回落，说明只改外层 sink 不足。P2 §3 点名 reader 内部也须显式 recipient／metadata／经来源判定的 null；LegacyReplay 只准旧 route 暂存并最迟 T-011 清零。`Protocol/Game/ReplayStateProjectionDelivery.cs:36–61` 原来先等待 ReadReplayStateCommand，再 MirrorMerge／Create／batch／Town；P2 §4 与 T-004 要求普通 Write 直接领域读取后按该顺序在事务外执行，不重新 Main、不重 ACK、不 nested bus，也不启用 Compose。该迁移决定承接原事务和投影边界。
- **新窄接口绕开 RecordedOutbound wrapper，让旧注错变成假绿。** 实际 `Server/tests/Support/RecordedOutbound.cs:317–369` 的 Registering 在真实 adapter 前设置 inFlight，并执行 callback／观察；`OutboundRegistrationParts.cs:82–101` 的 FaultingEncoder 真正在 EncodeMessage／EncodeBatch 中抛出。P2 §5 要求新旧接口 alias 同一 Registering、同 provider／outboxes，保这些路径，不能直接解析底层 adapter。Verification 还保持 `EffectOperationReplayTests.cs:97–126,219–227` 的扣减／当前数量／帧字节／无重复 ACK，新增同 cause、Committed、无新增 fence 和 source-close 顺序证据。`Application/Execution/Outbox/GameplayOutbox.cs:213–240` 的原已提交条目后追加 source close 也被明确保留；fixture 重放未被扩大为真实关闭 socket 可收包结论。

四个 routing override 分别指向本轮 B2／P2 challenge 和 review，未改变 controller、profile、gate 强度；Next Planning Boundary 明确 gate 与 amendment-only checkpoint 后才授权首次 effect/Main。实际 `CommandBus.cs:126–127`、`CommandWorkUnits.cs:41–44` 与 `CommandRequestContext.cs:97–99` 仍禁用新 effect/Main／缺工厂，符合当前阶段边界，不拿尚未完成实现反向阻断本轮 Plan 审查。

## 证据边界与移交

本轮仅静态读取及核对制品 SHA；未执行产品测试、build、native 程序或 git 操作。唯一写入为本新记录，写后停笔。现有实现仍需后继 Task 完成上述收口及来源证明，不能把本挑战结果或 B2 approved 当作产品已验证；独立 Plan reviewer 须自行决定本精确目标是否可批准。
