# B5／P6受影响门：主控消费与规划检查点

唯一workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`；写前HEAD为已committed done TASK-30 `d3a5fec85b9ecaf633efd2157b65946928b008cf`，index空。主控完整消费当前Spec新增WHAT/相关RQ-AC-VC/修订、B5 amendment/immutable challenge/fresh independent review；实际读P6全部受影响中心HOW/验证/路线及R1→R2差异、两版amendment、immutable CH-1和fresh独立Plan review，复核精确目标及真实Mailbox/DomainTransaction/Scope/WorkUnits/Outbox Begin/Register/End/Close源码。以下只释放匹配gate，不代实现或最终Spec验收。

| 精确制品（相对B） | bytes | SHA256 |
|---|---:|---|
| `spec.md` | 53754 | `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790` |
| `verification/spec-b5-amendment.md` | 11251 | `70d86067ba8ebdba9e5df0fe1e0efb118d5c57ba4d03919ec6d2920863eb20ec` |
| `verification/evidence/spec-b5-amendment/spec-b4-before.md` | 42632 | `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60` |
| `reviews/spec-b5-challenges.md` | 6854 | `5d25a35f5bfa08e074ed7b20d268c3804246273ffb80e0c26df4a66bf99fa701` |
| `reviews/spec-b5-review.md` | 9675 | `e85be4d072f30e8c3b24c1965a5e6d90902d10559c6d812e9505fb1dc1a80e66` |
| `plan.md` | 93207 | `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691` |
| `verification/plan-p6-amendment.md` | 9487 | `f0b5f1a90ea1283653a7691dc69bbfb153c96422a1c8c43f2f928a4c0281c96c` |
| `verification/evidence/plan-p6-amendment/plan-p5-before.md` | 71863 | `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| `verification/evidence/plan-p6-amendment/plan-p6-ready-r1-before.md` | 89736 | `eaba6ae255cbcb8120148644744e06b6097b2ea717e143f24c01b7a96b48e043` |
| `verification/evidence/plan-p6-amendment/plan-p6-amendment-r1-before.md` | 6993 | `f6920be220905ba1951a41b9a78a62f3bb04f9bab426c581206b1fca0eb96a47` |
| `reviews/plan-p6-challenges.md` | 9554 | `d0d165bede921fb70a156ed945aa31f6e3f2268612841ea0e318fb53fde3f04e` |
| `reviews/plan-p6-review.md` | 14304 | `96e8ab66b3a8ae76ecd8a8025a15c1d65aa4081dace2ef961d7b88301b3c1d19` |

Spec B5 challenge high0/low0，另一fresh Spec review **approved**。Plan CH-1针对R1 **confirmed**：真实selfRollback使Current=null并End原outbox unit/Close(open=null)，下层同provider领域proxy在outerobserver接错前可登记或Begin第二写事务；直接delegate无catch约束不能封该窗口。主控在review前只作一次最小高风险R2纠正；独立Plan review认定R2已充分解决并 **approved**，无未解决blocking。

R2要求observed AbortAction入口、原Tx仍Open时同步pin原真实unit/owner admission，早于settled/Currentnull、任何rollback await/callback/End→Close；真实完成后才签fact，owner.Close不清pin，observer只消费/交接。下层Register/Begin guard的独立occurrence不能被后续fact或重抛旧cause冲掉，显式runner全cleanup/健康检查后才解除健康候选。barrier只unit/pending/自身guard，无request/job/session/callback/newslot；Tx只存自身操作fact，显式request不回藏ambient。门后必须实跑两个下层proxy窗口及延后pin或Close清pin的可编译行为mutation；本次源级规划判断不是已运行的产品bug或已修产品。

B5只Mailbox六NonEffect：原success/normalrefusal/健康完整确认回滚响应、当前门及空selected范围保持；C1 durableUncertain/C2双因/C3独立scope等默认失败、精确物理source-close/chain。共享RunAsync保即时自结算/standalone-ambient caughtCommitthrow，只修typed错误图原action+Rollback双因精度；ItemDelivery/GM整批prepared-audit/QuestHelper/Housing/Wedding原子性/LiveOnly不改。whole scope前已登记success/normalrefusal按实际原unitstate/顺序保留后关闭，不承诺零帧/撤条目/earlyflush/第四raw。无Cera新增主动actor/footprint/Fence/Discard；正常disconnect teardown照旧，controlledtransport零Discard不外推真实全断线链零调用。原五WriteResult consumer/defaultWrite-ReadOnly/commit-only来源、3raw全部owner/cleanup、Compose禁用/F132、F131已披露精度、A/Contract/baseline保持。

Task30 report将single/multi合写各4帧是已提交文字精度错误；主控、B5挑战/独立SpecReview和PlanReview均实际按15B头解析final03：single005E16B+2×000E119B=3，multi013B16B+3×000E119B=4。新B5 amendment明确后继勘误；原report/Task/review/raw不重写，原raw/checker和调查Outcome不受影响。Task30十四场/59focused是已接受before，9029库存只引用Task29，不冒本次测试、八freshfull、TCP/A21或单CMD两秒测量。T004余8消息7fine、Rental2Main+独立refresh、T006～012仍有责；拍卖无入站不凭包名造业务。

主控复核B4/P5逐bytes快照分别等本HEAD原spec/plan；B5/P6目标及两R1snapshot原身份、17RQ/11AC、所有本次authored行尾/whitespace。新实现仍须同provider真decoder/Domain/SQLite/outbox/productionSender、保原强断言和四fine测试caller真实驱动、110表/全列NULL/BLOB回滚、真实native787与action来源、source关闭链/替换隔离、guard和同cause独立cleanup、八freshfull/DFO74-7/Golden15/Char10/注册与数量账。仅Mailbox6message+旧fine七canonical行可在后继有证后关闭。

root Contract r4 `d3d1bc4d244388c975f7ef05573c5f867be5fe90ea6b8eb3ac5ce61a586e2ef4`、childr1 `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6`、Specbaseline `10a7218a779493494b138bd6b43cc0fc1a84aa6a`及A seal不变。root/child Goal允许经审查有意变化及既有auto gate，当前无新用户HITL。routing仅四current artifact overrides和本次消费说明更新，gates值不动，旧B4/P5历史段保留；本次routing SHA `3eeaeaf363b8a9bdc2b089954d295699581ec053b1d630be9450a4ef54d00a7a`。

全部writer/挑战者/reviewer已停止，native本阶段未运行。本14路径amendment-only检查点只含Spec/Plan及amendments/快照/独立挑战与审核、本记录和routing；精确Git NUL集合/非owned HEAD bytes/full cached whitespace/父提交/footer/clean在提交前后核。检查点成功后才物化下一unused TASK-31，依赖/attention/readiness和required fresh result review照旧。没有提前产品/测试/ledger/Task31、push/PR/merge/remote/version/tag/deploy/游戏/cleanup或Spec完成主张，Astra与用户明确收尾门保留。
