---
kind: rks-review
verdict: approved
---

# Spec B3 独立审核：CollectionBox 第三具名登记出口

## 目标与审核身份

本审查由 fresh reviewer 独立完成；本人不是 Spec producer 或 challenge generator，未继续委派。使用 `reviewing-work/SKILL.md` 及 `references/spec.md`，判断 WHAT、范围和 acceptance 是否足够明确、能否保留既有行为。主控的裁定和挑战数量均不作为批准依据。

工作区为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，源码 HEAD 为 `dba3ede68e15f892afd35534db9fed7ed00d04a8`。以下路径相对本子 B 目录；审核前实际读取并核对：

| 输入 | bytes | SHA-256 |
|---|---:|---|
| `spec.md`（B3，唯一审核目标） | 32948 | `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05` |
| `verification/spec-b3-amendment.md` | 10325 | `8cdcbb991a4ccbe202a965bcac8433e6ee816804a8ba9a3b4519c007c2cb89b2` |
| `verification/evidence/spec-b3-amendment/spec-b2-before.md` | 29265 | `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228` |
| `reviews/spec-b3-challenges.md` | 7006 | `d61c61fa5f8aad20a3518b45619878b38927027a9e280d3da881f5750cf80fac` |
| `plan.md`（现有 P3，仅核不冲突边界） | 52018 | `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519` |

B2 快照与 `git show HEAD:<B>/spec.md` 逐字节相等；已直接审阅 B2→B3 实际 diff。约束来源实读根 TaskContract r4 的 Goal、Scope B、SC-13，子 TaskContract r1 Goal/SC-13、根 map 的 A seal/B 激活与持续约束、父 Spec RQ-023(c)/024，以及当前 AGENTS 中层次、协议类别和行为覆盖约束。根/子契约没有因本次修订变化。

审核范围仅为第三出口及其对 RQ-006、AC-003/004/005、VC“其余”和默认保护的直接影响；没有重审不受影响的整个 Spec 或旧已接受 Task。未要求尚未产出的 P4/TASK-20 实现，也没有把现源码尚无第三出口当成 Spec 缺陷。

## 结论与决定性证据

**approved：本范围内没有未解决的阻断缺陷。** B3 描述的结果与根保行为要求一致，成功出口、失败来源及反例边界可区分，后继 acceptance 足以判定是否正确迁移。本批准仅通过当前 B3 的 Spec 审核，不证明产品已经实现或验证，不批准 P4、TASK-20 或下一阶段产品写入。

1. **这是原成功登记点的有界保留。** 实读 `CollectionBoxSlice.cs:18–38,50–58,103–137`、`ProtocolModule.cs:326–333`、旧 `SelectedSessionSettlementBehavior.cs:18–25` 和 `EffectOperationContext.cs:102–107`：fine 为 Transactional，足迹验证及库存槽位记录仍在领域调用边界；主结算/Settled 返回以后，handler 才检查 current、组装库存/clock、Full boxes/selected、ACK，并对原物理 context 一次 SendBatch，拒绝 batch 在另一分支。Spec `:77–85` 保住该分段与顺序，同时明确拒绝、业务组帧、current/lookup/guard/Register、其他协议均不在 raw 出口内；没有把编排搬入 whole 当作改变旧失败保护范围的理由，也未增加主结算权。

2. **许可与真实来源必须同时成立，默认规则未被替换。** 实读 `OutboxAdapters.cs:66–85,159–183`，EncodeBatch 在 Register 之前，窄 encoder 调用与 outbox.Current/目标检查/Register 可分别观察；现 batch token 只接受 replay，`CommandRegistrationAttempt.cs:7–38`、`ExplicitEffectRequest.cs:54–59` 也只有原两出口，不能拿 replay 代替首次成功。B3 要求已登记具名许可、不可伪造单次凭据，绑定 request/job/阶段/调用/精确物理目标；要求最终只有同一原 cause 时才 raw/Committed/无新增 fence。其余错误仍 typed/aggregate 且保护全部精确参与者。这个机制边界无需在 Spec 中预定新 enum/API 名称，Application 也没有因此获得认识具体 Protocol CLR 类型或隐式当前请求的授权。

3. **独立错误不能被对象引用相同掩盖。** 实读 `CommandRequestContext.cs:73–124,145–162` 和 `CommandWorkUnits.cs:160–197`，传播去重与最终 raw 判定确有混淆失败发生次数的风险。B3 `:79–81`、AC-003 `:154` 要求所有 work/drain/unit/scope cleanup 完成后终判，独立阶段再次抛同一 cause 也撤销 raw，保留各因/独立发生事实并围栏全部参与者；业务 catch 不能继续写或成功结束。现有 P3 `:73–75` 的同一收口原则得到保留，没有削弱 sticky 默认或 commit-result 的窄恢复边界。

4. **证据强度没有被夸大，后继验收能发现实际退化。** 实读 `CollectionBoxNetworkSettlementTests.cs:31–50,118–172`：Select/Put 手造 request 直接 dispatcher，首次只断言 IOException 类型，持久结果及后续当前 replay 支持原行为，但没有直接 cause/entry/Ordinal/fence/source-close 观察；Withdraw 此处是拒绝。再读 `NpcShopPurchaseTests.cs:575–595`、`RecordedOutbound.cs:71–88,320–352,423–427`、`OutboundRegistrationParts.cs:88–101`，故障确从同容器真实登记 adapter 的 encoder 进入，Discard 不执行真实 socket 关闭。B3 如实写明这些局限，并在 AC-003 要求迁移前真正旧 decoder 的固定 cause oracle、迁移后同容器路径及直接 entry/原 Ordinal/Committed/失败 batch 未登记/fence/source-close/current replay、不重复结算/原 ACK 的对应证据，还要求默认保护、错误许可、独立同对象 cleanup 和有效 provenance mutation。它既没有将旧间证当直接验收，也没有把注错挪到 physical transport。

## 挑战裁定

匹配挑战集没有保留 CH（0 high/0 low）。我仍逐项检查其五条已攻击路径，独立裁定如下；这里的 rejected 指未证成“当前 Spec 存在阻断缺陷”，不是取消相应后继验证义务。

| 路径 | 裁定 | 原因与后继处理 |
|---|---|---|
| 一般 IOException/业务或拒绝 batch 借许可逃逸 | rejected | RQ-006 的三出口、AND 来源条件和明确排除项闭合；AC-003 的默认保护反例可识别放宽。 |
| 提前 raw 终判、同对象独立 cleanup 被去重 | rejected | RQ-006/AC-003 已明确最终收口和独立发生事实，保持 P3 收口原则；具体实现/动态证据 deferred 给门后 Plan/Task。 |
| 跨 request/job/阶段/调用/物理 target，closed/leaked/reuse 或借 replay | rejected | 单次 opaque attempt 与已登记许可同时要求，明确不得借 replay；失败后禁写/禁成功。 |
| 旧 fixture 被当成 cause、ledger 或真实关闭的完整证据 | rejected | Spec/B3 amendment 明示局限，并要求先旧后新直接对应证据；这批未运行验证 deferred，当前不声称完成。 |
| Select/Put 动态结果被外推为成功 Withdraw 已验收 | residual-risk | 共享成功分支支持其属于同一语义出口，但没有现成成功 Withdraw 动态注错证据；B3 明示不冒称覆盖。后继声称覆盖成功 Withdraw 时须有实际证据，此残余不阻断 WHAT/acceptance。 |

## 保持项与交接边界

实际 diff 未改根/子 TaskContract、A、baseline 或旧结果；B3 仍为 17 RQ、11 AC，VC-B1/B2/B3 不变。第三出口计入 VC“其余”的旧行为保留，没有暗加新可见变化。B2 修订记录保留为历史，没有把“两出口”旧叙述偷换成当时已有三出口。P3 的 commit-result、普通异常 sticky、Compose 禁用/F-132 未修边界保持；P3 仍写原两出口属于待受影响修订的方法稿，不能凭本批准直接启用第三出口。

控制器后续仍须按 B3 修订记录另行授权 P4 受影响方法修订，取得匹配 fresh Plan challenge/review，再做 amendment-only 检查点，之后才规划 TASK-20/产品。共享核心改动的完整 Release build、八项目 full 与独立结果审查是后继验收义务，本轮没有新 native/build/test 结果。TASK-01～19 和 A 的接受状态未被本审核重开或重新证明。

本轮仅执行只读 shell/git/哈希核对，唯一新写入为本文件；没有 native、构建、测试、产品/Plan/Task 修改或 git stage/commit。审核完成后停止共享写入。
