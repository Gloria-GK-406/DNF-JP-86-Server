# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`
- Intake review: `./task-contract-review.md`，精确绝对路径与revision1匹配，approved；父契约r4及A seal e6f55558未变。

## SDD Readiness

用户明确继续，根结果B六条件、A已接受起点、18项交接和T1～T6清零目标均稳定。局部接口/迁移拆分由Spec/Plan及Task安全决策，不能改变保护/事务点等价。F130/F127首单元、F88/F93先保护、F132先处理的边界已明示。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 本子契约是一项可独立封存/回滚的协议编程模型结果，按父Multi controller只能Single。直接推进时，跨大量切片迁移容易遗漏单例请求状态消费方、具名判定或结算边界，codec/金样本本身未必发现重复持久结算；Spec覆盖、渐进Plan和独立完整复核值得其时间成本，符合用户原governed精度选择及本次标准RKS续作要求。整体A/B结构不重选。
- Reroute conditions: 只有已批准边界/保护/提交点无法成立，或出现独立接受/回滚边界才回父控制器；单条CMD超过2秒等数量级测量问题按根决定回报用户。普通有界缺陷留本Spec修正，不重开A。

Parent controller为`../../multi-spec-map.md`；父激活map检查点已完成，Single Spec已在下方补全Controller Selection/Execution Configuration，Spec baseline取该map提交。产品实施仍须先通过Spec/Plan及规划检查点。

来源草案已从根verification/reroute-t008-spec-002-draft.md逐字节移为本目录source-draft.md（A子路由明确排除的根输入）。该迁移及本契约/审查/路由由B规划检查点纳入，不改写A封存制品。后续Spec/Plan/tasks/reviews/verification均仅在本目录，不复用A可变路径。

## Controller Selection

- Controller: `sdd-governed`
- Profile source: `long-running`
- Rationale: 沿根RI-10明确governed控制。Standard的Spec/Plan/最终审查不足以单独守住已知缺少保护用例的当前性/结算状态，以及跨139个非遥测handler的长期迁移偏离；独立挑战和累计偏离审查有具体精度收益。持续分簇交付和多上下文恢复需要long-running，逐Task渐进细化而非一次写完全部局部设计。

## Execution Configuration

- Profile: `long-running` with `sdd-governed`; long-running: `yes`；attention_review: on-drift。
- Spec baseline: `10a7218a779493494b138bd6b43cc0fc1a84aa6a`（唯一spec-002激活map检查点）。复用父工作区/分支，无新隔离。
- Gates:
  - spec: none
  - spec-adversarial: none
  - spec-review: auto
  - plan: none
  - plan-adversarial: none
  - plan-review: auto
  - implementation: auto
  - drift-check: auto
  - change-adversarial: none
  - final-review: auto
  - verification: auto
- Bound methods: implementation: `test-driven-development`（先应用test-value gate；结构搬迁沿用行为测试/编译/结构判据，不制造编译失败当RED）。
- Artifact path overrides（本次执行中B2/P2 amendment的受影响gate重跑；旧B1/P1审查原文留存，不改变controller/profile/gate强度/契约或baseline；其余制品仍使用共享composer默认路径）:
  - spec-adversarial: `reviews/spec-b2-challenges.md`
  - spec-review: `reviews/spec-b2-review.md`
  - plan-adversarial: `reviews/plan-p2-challenges.md`
  - plan-review: `reviews/plan-p2-review.md`
- Parent controller: `../../multi-spec-map.md`。
- 继承任务授权覆盖普通实施和本地检查点，不再重复规划后人工停点；Astra及分支收尾仍是用户保留门。
- 初始未提交输入仅B目录及原草案到source-draft的迁移，均为本Spec拥有，规划检查点纳入；不修改A封存文件。
- 全量核验在允许本机系统服务/FSEvents的环境运行，采用console detailed和DFO_GAMEPLAY_ASSERTIONS落盘；必要时-m:1串行项目，不改xUnit类并行、断言或原期限。沿用两条已知内容基线，套件耗时/内存只记录。
