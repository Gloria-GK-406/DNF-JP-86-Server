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
- Artifact path overrides（本次执行中B6/P7 amendment匹配的受影响gates已独立approved；旧B4/P5、B3/P4及更早审查原文留存，不改变controller/profile/gate强度/契约或baseline；其余制品仍使用共享composer默认路径）:
  - spec-adversarial: `reviews/spec-b6-challenges.md`
  - spec-review: `reviews/spec-b6-review.md`
  - plan-adversarial: `reviews/plan-p7-challenges.md`
  - plan-review: `reviews/plan-p7-review.md`
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


## B6／P7受影响gate消费（2026-10-25）

主控完整消费当前Spec B6 ready R1／Plan P7 ready R1、各自fresh immutable challenge（均0high/0low）与分别fresh independent approved review，精确身份/边界见`verification/spec-b6-plan-p7-controller-consumption.md`。本轮仅六消息VC-B6及相关HOW：先独立NoRoom正常确认回滚记账修复，再按旧encoder oracle→新whole有用闭合，保各自恢复/独立刷新事务及默认精确保护/原链/存活帧；无新raw类别/Self/Compose。十实际WriteResult原consumer保留、后继仅四opening具名，Rental异常不恢复；其他owner/契约/A/baseline/profile/auto门保持。先amendment-only本地检查点固定，再详细规划Task33；未提前建Task/运行native/写产品/闭canonical。旧B5/P6消费只保历史，不替代本门，Astra/用户收尾及不pushPR等保持。


## 执行配置调整（2026-10-25，主控方）

根据用户 2026-10-25 的要求，自 TASK-33 起按 `../../process-reset-2026-10-25.md` 执行。controller（`sdd-governed`）、profile（`long-running`）、Spec 基准、契约和 17 RQ／11 AC 都不变；只改各个门在什么时候触发：

- 保持原行为的迁移不修订 Spec／Plan。有意的可见行为变化，在 Spec 的有意变化表追加一行，随该 Task 的结果快审一起审。
- Spec 正式修订只在验收边界、根契约约束或中心方案变化时才做，且只做一次独立评审；Plan 修订只在簇顺序或跨簇共享契约变化时才做。
- 对抗挑战（spec-adversarial、plan-adversarial、change-adversarial）只在 spec-002 完成后的完整结果评审时做一次。
- 不再有只取证的 Task；旧行为以正式测试钉住。原始证据不进 git。
- Spec 基准改写后为 `51bd1e9cb7b4a6ace5b0b05b098e7e50adc1cdea`（原 `10a7218a`，见 `../../history-rewrite-2026-10-25.md`）。

## TASK-39：P8 受影响 Plan 门（2026-10-25）

Plan P8 ready R1 的独立审核 `reviews/task-39-plan-review.md` 已 approved，主控消费记在 TASK-39。增量只补两塔所需的显式多 owner 无账本足迹，并与原失败保护 Participants 分离；Cera 单 actor、原保护范围、根／子、Spec 和中心方案保持。按新流程不生成挑战、不建取证 Task；既有执行者继续，唯一独立 reviewer 后续复核完整 Task 结果。当前 plan-review override 为 `reviews/task-39-plan-review.md`，旧 P7 门保留历史效力；本次结果／全量尚未完成。


## TASK-45：B7／P9 受影响门（2026-10-25）

独立 `reviews/task-45-plan-review.md` R1 指出 Spec RQ-009 未授权祭坛纯唤醒类别，主控接受并仅定点修订 B7；同一独立 Astra 在 R2 先批准 Spec B7 ready R1，再批准 Plan P9 ready R2。主控消费写在 TASK-45，先提交五份规划文档再实施接口／判据。当前受影响 spec-review 与 plan-review 均指向该文件的各自 R2 结论，旧门只保历史效力；按新流程无挑战、无额外取证 Task。根／子／中心不变，Timer 来源未闭合；此批准不代完整 Task 结果快审或最终八项目核验。


## TASK-48：B8／P10受影响门待审（2026-10-25）

主控依据实际card-delay仍调用SelectCard fine以及原作业内调度／取消时点，拟定Spec B8具名翻牌固定阶段生产者控制、Plan P10同实例／typed内部来源HOW，详见TASK-48。17RQ／11AC IDs、根／子和中心不变，按process-reset只做同Task唯一独立Astra的有界Spec→Plan审查，无挑战或只取证Task。当前仅ready待审；执行者可补跑旧正式测试，产品／接口／白名单在主控消费及规划检查点前禁止实施。结果快审仍由同一独立reviewer完成，用户外部收尾门保持。


### TASK-48规划门消费 R1

唯一独立`reviews/task-48-plan-review.md` R1先批准B8 ready R1，再批准P10 ready R1／Task48 revision1；主控已完整读取并接受，消费记录在Task48。当前受影响spec-review／plan-review均指向该文件各自结论，历史门保留效力。metadata-only检查点后放行产品／判据实施，最终Task结果尚未验收；不关闭其它来源、功能簇或外部收尾门。

## TASK-57：B9／P11 规划门消费（2026-10-26）

同一唯一 Astra 已分别在 `reviews/spec-b9-review.md` 与 `reviews/task-57-plan-review.md` 批准 B9 ready R1、P11 ready R1；主控已全文消费，记录在 TASK-57。当前受影响 spec-review／plan-review 指向这两份文件，旧门保留历史效力，无挑战。两塔来源分解依据根 Goal 的经审查有意变化出口、SC-8B 例外和本次独立核定，不借 F-07 自动授权；根／子／中心方案、单执行器与禁止 CMD 嵌套不变。

本次补显式内部 owner、纯取消的真实发生证明、累计已声明 physical 的引用去重和整轮 producer 排空；B9 明列调度／失败边界与旧缺陷修正。metadata-only 检查点后才放行产品，最终七行迁移、八项目全量与 Task Result 尚待完成。其余功能簇与来源不提前闭合，外部 Astra 与用户分支收尾门保持。
