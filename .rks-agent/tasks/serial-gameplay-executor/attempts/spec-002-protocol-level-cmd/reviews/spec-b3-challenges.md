# Spec B3 有界对抗挑战记录

## 身份与范围

本记录由 fresh 独立 adversarial generator 按 `generating-adversarial-challenges/SKILL.md` 生成；不是 producer 或 reviewer，不给批准/拒绝 verdict，也不证明实现或测试已经完成。

- 工作区：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`。
- 源码 HEAD：`dba3ede68e15f892afd35534db9fed7ed00d04a8`；开始时暂存区为空。
- 唯一目标：`../spec.md` 的 B3，32948 bytes，SHA-256 `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05`。
- 配套输入：`../verification/spec-b3-amendment.md`，10325 bytes，SHA-256 `8cdcbb991a4ccbe202a965bcac8433e6ee816804a8ba9a3b4519c007c2cb89b2`；前版 `../verification/evidence/spec-b3-amendment/spec-b2-before.md`，29265 bytes，SHA-256 `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`。以上身份先于源码攻击核对，全部匹配。
- 治理来源：根 `TaskContract.md` r4 Goal、Scope B、SC-13（80–95、135–165、235 行），子 `TaskContract.md` r1 Goal/SC-13（31–42、102 行），根 `multi-spec-map.md` 的 A seal/B 激活及持续约束（59–79、91–103 行），父 `spec.md` RQ-023(c)/024（78–86 行），当前子 Spec 与 Plan P3 §4/Verification。
- 攻击范围仅限新增 CollectionBox 第三即时编码登记 raw 出口及其对既有要求的直接冲突；17 RQ、11 AC、VC-B1/B2/B3、A、不确定保护、P3 commit-result、Compose 禁用与 F-132 边界保持。方法/API 名称属于后继 Plan/Task，不因尚未设计而报缺陷。

本轮只读静态检查 actual diff、实际源码和现有测试；没有 native、构建、测试、故障探针、git 写操作或产品修改。没有另派代理。唯一新写入为本记录。

## 保留挑战

**0 项：high-risk 0，low-risk 0。** 没有找到符合本次有界标准、需要作为 CH 保留的目标缺陷。以下为已攻击但不形成独立挑战的原因，供后续独立 reviewer 判断；它们不替代 review。

1. **把新 batch 许可扩成一般 IOException/业务豁免。** `CollectionBoxSlice.cs:103–137` 实际把当前性、库存映射/clock/Full 与 success batch 分开，拒绝另在 137 行登记；`OutboxAdapters.cs:66–85,175–183` 的 encoder span 又与当前 outbox/最终 Register 分开。B3 `spec.md:75–85` 只保首次成功 Main 结束后的原一次、原精确物理目标 batch，明确排除业务组帧、current/lookup/guard/Register、拒绝和其他协议，要求已登记许可及真实 adapter 的 encoder 出处同时成立。不能仅凭异常类型或 replay 许可取得该结果；未发现此处缺少可观察边界。

2. **同一异常对象掩盖独立 cleanup，或提前 raw 终判。** 现代码 `CommandRequestContext.cs:82–124,145–162` 存在传播去重和窄登记来源记录，`CommandWorkUnits.cs:160–197` 将 cleanup 与最终 post-commit 保护分开；单看 cause 引用不足以证明只有一次失败。B3 `spec.md:79–81,154–156` 已要求全部工作/drain/scope cleanup 后才终判，独立 unit/scope 再抛同一 cause 也撤销 raw，保独立发生事实并围栏全部精确参与者。P3 §4 已有相同收口要求。后继如何实现该发生事实是方法问题，不能因当前尚无第三出口实现反推 Spec 缺陷。

3. **许可跨请求、job、阶段、调用或物理连接复用。** 现 `CommandRegistrationAttempt.cs:7–38` 与 `ExplicitEffectRequest.cs:54–59` 只有两个出口且 batch 硬绑 replay（`OutboxAdapters.cs:159–165`），第三出口直接套旧 token 必然不适用。B3 `spec.md:79–85,154` 已单列第三出口及 request/job/阶段/调用/精确物理目标绑定、不可伪造单次凭据、closed/leaked/reuse 与 catch 后禁写/禁成功约束，且禁止借 replay。无需在 Spec 层预定 enum/API、注册与 adapter 的具体改法。

4. **把旧 fixture 重放当作 cause/ledger/source-close 的完整证据。** 实际 `CollectionBoxNetworkSettlementTests.cs:31–50,118–145` 手造 metadata/request 直接 dispatch，`RecordedOutbound.cs:423–427` 的 Discard 不做物理关闭；`RecordedOutbound.cs:71–88,337–352` 和 `OutboundRegistrationParts.cs:88–101` 则确实经同容器真实登记 adapter 的 encoder 注错，不能改称 physical transport。B3 `spec.md:83,154,195` 明说这些局限，并要求后继真正旧 decoder 的 Select/Put 固定 cause oracle、同容器新路径、直接 ledger entry/原 Ordinal/Committed/fence/failed batch/source-close/current replay 证据与可编译有行为价值的 provenance mutation。后继尚未运行这些验证，不能在本次 Spec gate 冒称已完成；但这不是遗漏验收义务。

5. **Select/Put 结果过度外推至成功 Withdraw。** `CollectionBoxDomainService.cs:106–118` 的 Withdraw 有实际物品到达动作；`CollectionBoxSlice.cs:121–134` 之后共用一次 success batch，故静态上属于本出口。现测试 `CollectionBoxNetworkSettlementTests.cs:148–172` 只在 Withdraw 拒绝分支检查响应。B3 `spec.md:83,195` 和配套记录 `spec-b3-amendment.md:23,31,51` 保留这一证据层级差异，没有把 Withdraw 写成已动态注错。把尚未实施的成功 Withdraw 新测试列为当前 Spec 缺陷，会混淆 Spec 与后继 Task 的职责；当前没有据此保留挑战。

## 读取的关键源码身份

以下 SHA-256 在本轮静态读取后核对，便于 reviewer 区分实际源与作者叙述；路径相对工作区根。

| 文件 | SHA-256 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/CollectionBox/CollectionBoxSlice.cs` | `77be2e3ce11344fc6531a2576fdd440cf20e6631c025c68f298175bc6a75ddb8` |
| `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs` | `2d701fcf3fa8d125555c886f89bb8eee12918e3c62352f1f19b8444dbbd867a1` |
| `Server/src/Dfo.Server.Application/Commands/CommandRegistrationAttempt.cs` | `9e2547994fc5de1bd24daf6a38dd03e2b89c89b9501bae3c3288125d048e84a8` |
| `Server/src/Dfo.Server.Application/Commands/CommandRequestContext.cs` | `50fefc740334b04fbdabddd29a38f5f7516781c7f0b3b6239686a39d99052e1a` |
| `Server/src/Dfo.Server.Application/Effects/ExplicitEffectRequest.cs` | `558746137a00a6a86d3f2790537ff5d3d9ddff731bc05b832a073367984c58fd` |
| `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs` | `50336652fad2b84338641bd9243fb9fa48ada532982779f05c91124ce3ea4817` |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/CollectionBoxNetworkSettlementTests.cs` | `6900798ecb4a160ca873908567ff19ecdaf36e94300a140af579e54d1792f4a2` |
| `Server/tests/Support/RecordedOutbound.cs` | `0a808005393fc54ab179ffeacdd7ae8654d402896a852a7913fa058d15f993a6` |
| `Server/tests/Support/OutboundRegistrationParts.cs` | `3d8cddb5c1435857a6b2c9920e6d02b2365acb4ea9f059626753dc8dc9155dda` |

本记录落稿后保持不可变；本生成上下文停止所有共享写入，由另一个 fresh reviewer 独立给出匹配 B3 的 review。
