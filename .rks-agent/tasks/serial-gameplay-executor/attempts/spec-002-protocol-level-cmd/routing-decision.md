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
- Artifact path overrides（本次执行中B5/P6 amendment匹配的受影响gates已独立approved；旧B4/P5、B3/P4及更早审查原文留存，不改变controller/profile/gate强度/契约或baseline；其余制品仍使用共享composer默认路径）:
  - spec-adversarial: `reviews/spec-b5-challenges.md`
  - spec-review: `reviews/spec-b5-review.md`
  - plan-adversarial: `reviews/plan-p6-challenges.md`
  - plan-review: `reviews/plan-p6-review.md`
- Parent controller: `../../multi-spec-map.md`。
- 继承任务授权覆盖普通实施和本地检查点，不再重复规划后人工停点；Astra及分支收尾仍是用户保留门。
- 初始未提交输入仅B目录及原草案到source-draft的迁移，均为本Spec拥有，规划检查点纳入；不修改A封存文件。
- 全量核验在允许本机系统服务/FSEvents的环境运行，采用console detailed和DFO_GAMEPLAY_ASSERTIONS落盘；必要时-m:1串行项目，不改xUnit类并行、断言或原期限。沿用两条已知内容基线，套件耗时/内存只记录。

## B3／P4受影响gate消费（2026-10-25）

主控完整消费当前Spec B3／Plan P4及分别匹配的fresh challenge与独立approved review，精确身份与边界见`verification/spec-b3-plan-p4-controller-consumption.md`。本次仅原CollectionBox首次成功batch实际encoder的第三具名出口，根／子契约、Spec baseline、A、全部既有Task与门强度不变。需amendment-only本地检查点之后才指定临近Task；没有提前启用产品、创建Task或把后继旧新oracle／guard probe／八full记pass。原foreign attempt catch后继续风险及成功Withdraw动态边界继续由受影响单元实证处理，不新增用户HITL。

## B4／P5受影响gate消费（2026-10-25）

主控完整消费当前Spec B4 ready R2／Plan P5及匹配fresh独立gate，详见`verification/spec-b4-plan-p5-controller-consumption.md`。本轮仅Cera：原正常和健康确认回滚响应保持；新增VC-B4明确默认失败下单精确买家的足迹／参与者、Fence／精确连接阻断／DiscardExact／来源关闭及既有出站次序。Spec CH-1在R1成立，R2一次最小纠正后独立approved；Plan fresh挑战0/0且独立approved。先以amendment-only本地检查点固定这组门，再指定Task29；没有当前产品／测试改动或未来通过主张。baseline、Contract、A封存、controller/profile/auto门强度及三个raw出口保持，旧B3/P4消费段仅保历史。

## B5／P6受影响gate消费（2026-10-25）

主控完整消费当前Spec B5与Plan P6 ready R2及匹配fresh独立gate，精确身份/范围见`verification/spec-b5-plan-p6-controller-consumption.md`。Spec challenge0/0、独立review approved；Plan immutable CH-1在R1成立，主控据实际Unit.End/Close/Scope及下层proxy窗口作一次最小R2纠正，另一fresh reviewer确认充分并approved。旧R1Plan/amend逐bytes保留、挑战不改不重生。先以amendment-only本地检查点固定，再详细规划Task31；没有产品/native通过或提前Task。Mailbox保原即时自结算/共享原子性/健康确认回滚ACK，VC-B5默认失败/精确来源关闭和共享双因保留；正常disconnect照旧，不增Cera actor/footprint/Fence/Discard。self入口/来源/早pin admission/完整cleanup按P6，原五consumer/3raw/Compose禁用/A/Contract/baseline/profile/auto门保持。旧消费段仅历史，不代本次门；Astra及用户收尾/不pushPR限制不变。
