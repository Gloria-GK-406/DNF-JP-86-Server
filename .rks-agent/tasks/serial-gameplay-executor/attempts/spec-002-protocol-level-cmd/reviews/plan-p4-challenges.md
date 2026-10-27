# Plan P4 fresh adversarial challenges：CollectionBox 第三即时登记出口

## 目标、身份与攻击范围

本记录使用 `generating-adversarial-challenges/SKILL.md`，由独立 challenge generator 在 fresh 上下文生成；不是 Plan review，不给 verdict，不代替另一位 reviewer。没有继续委派。所有路径除下列子 B 简称外，均相对工作区根。

工作区：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；分支 `task/refactor-session-20261021`；实读源码 HEAD `dba3ede68e15f892afd35534db9fed7ed00d04a8`。B 为 `.rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd`。

| 输入 | bytes | SHA-256 |
|---|---:|---|
| **唯一挑战目标** `B/plan.md`（P4） | 61551 | `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7` |
| 配套方法依据 `B/verification/plan-p4-amendment.md` | 16801 | `a3683f7449fc4bf9414c06f30d2447c4731bcaa64aea4a7c8830f199d70101bf` |
| 原 P3 `B/verification/evidence/plan-p4-amendment/plan-p3-before.md` | 52018 | `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519` |
| governing Spec B3 `B/spec.md` | 32948 | `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05` |
| Spec 批准身份 `B/reviews/spec-b3-review.md` | 8194 | `42d37ed6073dd53a1904a77e8b847501c5d468f19bcc17e737c42fd39c1c4a52` |

实读治理来源：根 `TaskContract.md` 的 Goal／Scope B／Constraints／SC-13／授权边界，子 `B/TaskContract.md` Goal／Scope／SC-13／Authorization，根 `multi-spec-map.md` 的 A seal／B 激活与持续约束，父 `spec.md:78–84` RQ-023(c)／024；当前 `AGENTS.md` 分层、Protocol 唯一接线、CMD 注入与显式请求、工作单元／footprint／两旧许可及失败身份规则。决定本轮结果要求的是 Spec B3 RQ-006 的第三出口及 `:67–85`、AC-003／004／005；Spec 批准不等于批准本 Plan。

攻击范围仅 P3→P4 实际 diff 中与 CollectionBox 首次成功 Main 后一次 exact physical batch 有关的 HOW：通用 SettledInitialBatch、状态穷尽、实际调用种类、现有 opaque attempt 透传、whole／Main／足迹与四 catch 迁移、来源证明和最终收口、安全矩阵与先旧后新证据。没有重审其它路线、重开已接受 Task、扩展 WHAT，亦未把作者自查 `manual_required` 当正式 gate 或追加用户停点。工作区无 `.codegraph/`，依仓库规则跳过 CodeGraph。

## 保留的挑战

**0 high-risk，0 low-risk；没有保留 CH。** 这只表示本次没有从目标方法中得到可成立的新挑战，不是实现正确、覆盖完备或 Plan 通过的结论。以下给独立 reviewer 留下实际攻击路径与未保留原因；不是重复列为挑战或取消门后义务。

| 攻击路径及实读依据 | 未保留为新挑战的原因 |
|---|---|
| 只加 enum／元数据，实际 adapter 仍硬绑 replay；或把非 Required 一律判为 replay，从而新出口永远拒绝或反向误放宽。现源码 `Server/src/Dfo.Server.Application/Effects/ExplicitEffectRequest.cs:54–59`、`Commands/CommandRegistrationAttempt.cs:7–38`、`Execution/Outbox/OutboxAdapters.cs:159–173` 确为旧两分支。 | P4 `:71–79` 已选择穷尽 switch、batch 只接 replay／initial、required 独立固定、未知值拒绝，并明确替换实际 adapter 的硬绑点；`:202` 有错误 phase／调用种类双向反例。它不是只声明名称的空壳。内部方法拼写和局部校验核心可在详细 Task 安全收敛。 |
| initial 许可被用于拒绝、Full／clock／typed 转换／Register 或其它协议；测试绕开 Registering 令 encoder 来源变成自报。实读 `CollectionBoxSlice.cs:103–137`、`OutboxAdapters.cs:66–85,175–191`、`Protocol/Game/IGameCommandOutbox.cs:18–31`、`Abstractions/Networking/ICommandMessageOutbox.cs`、`RecordedOutbound.cs:328–391`。 | P4 `:81–83,103–105,114` 将许可消费放在原成功 result 分支、全部业务组帧之后一次 batch，保持原 opaque 对象并禁止绕同 provider wrapper；仅 actual encoder 窄 catch 盖章，普通重载不变。`:198–202` 要实际触发与默认保护对照和有效 mutation。没有找到目标把非 encoder span 合法化的决定。 |
| request／job／物理 target 或旧 token 被借用，guard 被 catch 后新 request 仍写入／正常返回。现 `CommandRegistrationAttempt.cs:19–30` 只 Failed token owner，`CommandRequestContext.cs:47–54` 的 closed／job 拒绝直接 throw；`ExplicitEffectCommandTests.cs:357–412` 分别是未捕获 previous-token 和主要观察 owner-drain，尚不能证明新 request 的 catch 后禁写。 | 这是可信的实施检查点，但 P4 `:204` 已明确要求同 provider 真实新请求 catch 后实际持久写／正常 return 探针、同时观察 owner 和终局，若能继续则在显式边界最小修正。该局部行为可以在同一门后 Task 便宜区分并阻断交付；没有动态证据把它升级为当前 Plan 必然失败，也不据此要求 ambient、第二 Host 或第二凭据框架。 |
| Main 已提交就过早判 raw／typed；同一 cause 在独立 unit／scope cleanup 再次抛出被引用去重，少围栏参与者。实读 `CommandBus.cs:86–128`、`CommandRequestContext.cs:73–162`、`CommandWorkUnits.cs:160–197`。 | P4 `:85–87,202` 明确保留 drain→Close→scope cleanup→一次最终裁决，独立同对象不是正常传播，任何额外独立失败撤销 raw、保各因及全部精确参与者。现代码也已有独立 cleanup 发生标识；扩展无需提前改变终判。原 typed／默认 Write／ReadOnly／P3 恢复门仍纳入回归。 |
| whole 迁移吞 ordinary SQL 或扩大 catch，破坏 Main／minimum footprint／成功帧序；拒绝或空 actor 被错误提升为成功。实读 `CollectionBoxSlice.cs:18–38,50–58,103–151`、`CollectionBoxDomainService.cs:63–118`、`ProtocolModule.cs:326–333`。 | P4 `:81,149,198–202` 给出一次 Main 的原 action 顺序、Select 与其它 action 的 minimum、四 named catch／NoRoom 过滤、RequireRollback guard 不吞、ordinary SQL 外抛、空 actor／初始 Rejected 以及非空 whole 契约。原组帧与 batch 顺序明示；局部结果类型不预定本身不导致语义缺口。 |
| Select／Put 旧测试或三项目库存被冒称直接旧 decoder／八 full；无效 hook／编译失败 mutant 冒称保护；成功 Withdraw 或真 socket 关闭被外推。实读 `CollectionBoxNetworkSettlementTests.cs:31–50,66–109,118–172`、`OutboundRegistrationParts.cs:88–101`、`RecordedOutbound.cs:423–427`。 | P4 `:198–206` 要在迁移前真正旧 decoder Select／Put 固定 cause oracle、保原 source／driver／SHA，再以同 provider 新 whole 对应实跑；逐项直接观察 entry／原 Ordinal／Committed／fence／失败整 batch／source close／current replay。它还要求真实 physical replacement 触发、可编译且触发的 provenance mutant、新鲜八 full 与数量身份闭合。成功 Withdraw 未跑继续披露为已知 nonblocking residual，Discard 不关 socket 亦已限定，不能重新包装为本 Plan 新缺陷。 |

## 证据与交接界限

本轮仅静态读文件、P3→P4 diff 和只读 Git／bytes／SHA 核对；没有 native／probe／build／test，没有把计划中的 oracle、mutation、guard 探针或八 full 记为完成。产品迁移前的真实旧直接 oracle仍是门后 Task 的先行义务。若该 oracle 否定第三出口假设，P4 `:236` 要先交主控处理，不能换基线或扩大 raw。

本生成器唯一写入是新 `B/reviews/plan-p4-challenges.md`，不修改目标、配套 amendment、Spec、Task、旧证据、routing、产品或 Git index。写入前已再次核对上表 Plan／amendment／P3／Spec bytes 与 SHA、HEAD 以及 index 空。保留本记录为 immutable evidence；之后交另一 fresh reviewer，对绑定的 P4 独立作 verdict。本生成器交付后停止全部共享写入。
