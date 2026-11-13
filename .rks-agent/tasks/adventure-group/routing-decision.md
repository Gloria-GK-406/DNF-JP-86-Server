# Routing Decision

- TaskContract: `E:\Project\DNF\DNF-JP-86-Server-adventure\.rks-agent\tasks\adventure-group\TaskContract.md`
- Contract revision: `5`
- 评审：同目录 `task-contract-review.md`，`verdict: approved`，`contract_revision: 5`。评审记录的路径是主 checkout 下的同名文件；2026-11-13 建 worktree 时整个任务目录按原相对路径搬入本 worktree，SHA-256 逐文件核对一致后删除源副本，内容与修订号未变。

## SDD Readiness

意图、范围与验收都已稳定：用户已裁定删列、不分期、领域服务 owner 形态与现算；契约 SC-1–SC-9 每条都有可观察证据。执行方向已知（规则服务 + 领域服务 + 经验管线一项 + 四处发包 + 等级变更后推送 + schema v5）。剩余不确定只有 A21 `0x01A0` body，契约已列为假设并给出实机证伪的出口。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求一个任务契约做完，并在实机验收上要求精确（逐项编号测试、明确反馈）。Direct 下最可能出的错是漏项和耦合错误：四处发包口径、通关加成在三类塔结算中的不同处理、`0x01A0` 挂在可重放的 Reliable 订阅上导致重复加级、USERINFO1 与推送的先后、schema 迁移与测试三件套，这些分散在多个层，单靠最后一次检查难以兜住。Spec 固定验收、Plan 固定挂点与顺序、完整结果独立审查，能以可接受的时间换掉这些返工。所有内容共用一个验收与回滚边界（同一 schema 版本、同一领域服务），没有可以独立接受的子结果，所以是 Single。
- Reroute conditions: 实机证明 `0x01A0` 非空 body 且任务内无法取得布局（按契约 Requires Confirmation 回到用户）；发现冒险团值必须持久化才能正确推送（违反用户约束，回到意图对齐）；schema 迁移需要拆成独立发布（出现独立回滚边界，改走 Multi）。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 比 Lite 多出的 Plan 有实际价值：推送挂点（Reliable 订阅的幂等或改用 LiveOnly）、发包与推送的顺序、读取链拆除与 schema 迁移的先后，是几个互相牵连的早期决定，一旦选错会传到多个切片，最终审查时才发现返工代价高。不选 Governed：没有凭据、权限、隐私或财务影响；数据库风险由契约规定的备份副本与原地迁移需确认兜住，普通审查加新鲜验证足够。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `17eb734d48d4698f466285df002430b19db1f0ae`
- Gates: `implementation: auto`; `review: auto`; `verification: auto`。用户实机验收按契约 SC-4a/SC-5/SC-9 在 verification 中取得用户明确反馈。
- Bound methods: `implementation: test-driven-development`
- Artifact path overrides: `none`
