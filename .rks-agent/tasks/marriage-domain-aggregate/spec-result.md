---
kind: rks-spec-result
contract:
  path: ./TaskContract.md
  revision: 3
spec:
  path: ./spec.md
outcome: completed
terminal_stage: verification
---

# Spec Result: 婚姻单一实体与阶段记录

## Delivered

- 婚姻业务由单一领域实体 `MarriageDomainService` 承载，对外有 4 个接口，按调用方划分：Query、Couple、Venue、Quest，都由同一个实例实现。原来的 8 个实现类和其中 7 个旧接口已删除；婚前任务接口保留原签名。
- 实体内部由 9 个职责组件和 1 个纯规则类组成。组件之间互不依赖，每个婚姻 store 只有一个所有者，阶段和心意点只由 `CoupleLedger` 写入（按 revision 做 CAS）。这些约束由架构判据守着。
- 新增 `marriage_relations` 表，直接存储关系阶段（Proposed / Engaged / Requested / Preparing / Married / Closed，6–15 预留）。所有阶段判断都读这张表；03D0 state 由阶段投影得出，投影在 Protocol 侧。
- 启动时对已有库做幂等回填，旧表和旧数据保持不变；回填中的异常由启动审计计数并告警。心意点在过渡期双写旧列。
- 刻意修正 B1-1～B1-5：「是否为一对」统一由账本判定，并核对账号；`ViewAsync` 在缺少任务内容时不再抛异常。其余线上字节和业务规则不变。
- 文档：设计稿的落地状态、双端实机回归清单、`MarriageWeddingFlow-20261016.md` 的注记，以及 `protocol/` 中三处 CurrentImplementation 注记。

## Verification

- `verification/completion-evidence.md`：`decision: pass`，SC-1～SC-7 全部通过。
- `reviews/change-review.md`：独立审查 round 1 和 round 2 都是 `approved`，没有阻塞项。

## Repository Result

- Changed paths：33 个修改、8 个删除、36 个新增，涉及 Server/src、Server/tests、Document、protocol，以及本任务目录。
- Baseline or accepted result：基线是 `task/next-round-20261013` 上的 `5a0cbed0`。接受的结果以本任务分支上唯一的一个本地提交为准。
- Parent rollback required：no

## Artifacts

- 设计稿：`Document/MarriageAggregateDesign-20261017.md`（含落地状态与已知限制）。
- 实机回归清单：`Document/MarriageAggregateRegression-20261017.md`，由用户执行。
