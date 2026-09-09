# TASK-02 — 共享技能规则与点数

## Task Unit Contract

- Revision: 1
- Outcome and references: ../plan.md T-002；../spec.md RQ-003/004，AC-003/004。不可变规则实例、完整职业上限/费用/前置/免费声明与当前余额共用算法。
- Owned boundary: Abstractions 技能规则与内容契约，Data 技能事实，Infrastructure 技能解析，Application 技能规则与选角/余额消费者，相关测试、模块注册。不得新增技能协议、改数据库格式或运行数据。
- Dependencies: TASK-01 的职业值。
- Required checks: 真实 PVF 代表技能、零/多值上限、前置与成本、SP/TP/免费技能边界、入场及余额回归；记录命令/TRX，不提交。

## Execution result — round 1

**Changed:** 新增 `ICharacterSkillRuleService` / `ICharacterSkillRule`、静态 `SkillRuleCatalog` 与完整 `CharacterSkillPointLedger`；PVF 内容源改为按 byte job 缓存的只读目录（最多 256 个 key），规则按目录共享实例，移除角色等级和已学集合组成的 Question 缓存。技能规则提供说明/前置、合法职业与等级上限、CanLearn、累计 SP/TP、免费等级；直接提供 `GetAll(job)` 与 `GetTotalPoints(job, level)`。现有入场兼容定义由 Application 规则服务投影，携带共享 Rule；角色余额及选角都经现有 preparation 接缝进入唯一 `SkillPointLedgerRules`，PvP 初始化免费成长也委托同一免费等级算法。真实 PVF 行为测试迁到 Application 测试项目以跨过正确的内容/规则边界。说明在 `Document/CharacterSkillRules.md`。

**Why:** 多值 maximum 必须按职业取值；零不可提升到 1。觉醒正值覆盖与转职回落、技能 fitness 不作为职业白名单/费用折扣，均遵从已核对的参考语义。fixed 是嵌套块，空直接整数列表仍表示存在；base 缺失=0 依据 ServerS4A21 HEAD `2db3a135`，只标代码推导。免费基线取多来源最大值，投入用累计已学减累计免费，费用末值延续。未知已学规则保持存储事实与既有零投入兼容，但账本列出 MissingSkillIds/IsComplete，现有余额投影输出 warning；完整余额用 long，仅 ToProtocolMirror 做 ushort clamp。职业支持集来自 chr 的 growtype/awakening 声明，非法组合明确拒绝；旧暗骑初始顺序测试原供料 grow1 不在真实档案支持范围，已由主控确认改为 grow0，而未放宽规则。保持现有兼容 source 的必要入口，未新增学习/退款协议或写库用例。

**Owned boundary:** 在已通过 TASK-01 的未提交工作树上开始，起始 `git status` 见 `../verification/task02/before-status.txt`，起始 Server/Document 全文件 SHA-256 见 `../verification/task02/before-sha256.txt`。本单元 14 个交付路径的 before/after 哈希（含删除）见 `../verification/task02/owned-sha256.json`；TASK-01 独占文件逐一保持原哈希。唯一重叠文件 ApplicationModule 只新增三个技能注册行，剔除这三行后与起始 SHA-256 完全一致，等价起始文本保存在 `../verification/task02/before-ApplicationModule.cs.txt`。未修改数据库/schema、TASK-01 其他源码或 execution yaml，未提交。

**Checks:** 以下命令均在任务工作树根目录执行；真实档案环境为 `$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'`，档案只读。

- TDD 价值：错误职业上限会直接改变可学级别/过期契约对账，fixed 丢失会错误计算免费成长与投入；旧测试没有多值或嵌套块判据，真实技能输出可作为独立于内部接口形状的稳定 oracle。先加这两个行为测试，后改解析与规则。首次插入未匹配换行导致零个用例匹配（`red.log/red.trx`，exit 0），不算 RED；修正插入后同筛选执行得到两个预期断言失败：元素点燃 expected20/actual15、fixed expectedtrue/actualfalse。
- RED：`dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --filter 'FullyQualifiedName~Multi_value_maximum_uses_the_career_slot|FullyQualifiedName~Nested_fixed_level_block_is_recognized' --logger 'trx;LogFileName=red-behavior.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task02`，exit 1，2 failed / 0 passed；证据 `../verification/task02/red-behavior.log`、`red-behavior.trx`。后续规则测试是回归证据，不冒充先行 RED。
- GREEN 定向：`dotnet test Server/tests/Dfo.Server.Application.Tests --filter 'FullyQualifiedName~CharacterSkillArchiveTests|FullyQualifiedName~CharacterSkillRuleServiceTests|FullyQualifiedName~RemainingSkillPointTests|FullyQualifiedName~CharacterEntryInventoryTests|FullyQualifiedName~CharacterEntryRuleServiceTests' --logger 'trx;LogFileName=green-focused.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task02`，exit 0，71 passed / 0 skipped。`green-focused.log/trx`。
- 最终构建：`dotnet build Server/86JP.Server.slnx`，exit 0，0 warnings / 0 errors；`build-restored.log`。此前 `--no-restore` 因工作树若干未用项目缺 assets 文件返回 exit 1（`build.log`），正常还原后已解决。
- 最终 Application 回归：`dotnet test Server/tests/Dfo.Server.Application.Tests --no-build --logger 'trx;LogFileName=application-final.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task02`，exit 0，1158 passed / 0 skipped。覆盖真实技能案例、零/多值/觉醒回落、前置/累计费用、免费来源最大值、两页余额/奖励、宽数值、未知规则诊断和已有入场/当前余额回归。
- Infrastructure 回归：`dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --no-build --logger 'trx;LogFileName=infrastructure.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task02`，exit 0，260 passed / 2 skipped；跳过的是有 PVF 环境时刻意不运行的两个“缺失档案”测试。全部 `UnitTestResult` 单项已解析统计，见 `../verification/task02/trx-summary.json`，未将 TRX 文件存在等同于通过。
- `git diff --check` exit 0；静态检索确认 fixed 自动成长算术仅在 CharacterSkillRule、余额算术仅在 SkillPointLedgerRules；内容侧无等级/已学组合 key；所有已有不属于 owned manifest 的 Server/Document 文件哈希保持起始值。

**Handoff:** 源码写入已释放，请主控安排独立 quick review。动态事实入口可消费 `ICharacterSkillRuleService.GetLedger(CharacterCareer, int, IReadOnlyList<CharacterSkillPageState>, CharacterSkillPointBasisState)`；结果为 Page0Sp/Page1Sp/Page0Tp/Page1Tp 四个 long、MissingSkillIds、IsComplete 和 ToProtocolMirror。单规则 `Get(byte job,int skillId)` 返回共享实例或 null；`GetAll(byte job)` 返回只读共享规则目录；`GetTotalPoints(byte job,int characterLevel)` 返回 CharacterSkillPointBudget(Sp,Tp)，无需构造动态页。技能状态入口可先投影现有 PageState 供料，算法不需要另写一份。

## Independent Quick Review — TASK02-QR-20260909-01

- Reviewer identity: 独立子代理 `/root/review_skills`，首次审查上下文；未参与执行或修改实现。Reviewed execution: round 1；Task Unit Contract revision 1，无附加修订。核对引用：`../spec.md` RQ-003/004、AC-003/004，`../plan.md` T-002，以及 TASK-01 职业值依赖；完整动态存储验收仍属于 T-003。
- Owned-diff identity: HEAD `8b19eaac9863e23da4703fa644bfd0cb882b6cd6`，以 TASK-01 未提交结果为起点；`../verification/task02/owned-sha256.json` SHA-256 `52C6F2EF774FF7AF272E88DA726F99038ADE923F41AE91038B64892C8698B248`。逐一核对本单元 14 路径（13 个当前文件与 1 个删除），after 哈希全部一致。ApplicationModule 去除三条技能注册后与 `before-ApplicationModule.cs.txt` 全文一致，未将整个 HEAD diff 当作本单元边界。
- Review evidence: 已读上述路径实现、测试与文档及适用 AGENTS.md。真实技能准入、多值/零上限、前置、累计 SP/TP、免费来源最大值与两页奖励账本有对应实现和行为判据。静态缓存仅按 byte job；规则构造复制并包装费用、上限和前置数组，生产目录与职业集合冻结。PVF 未声明职业明确拒绝，暗骑 grow0 与实际支持声明一致。未知已学规则保留事实及零投入兼容，同时返回 MissingSkillIds/IsComplete，已有余额投影记录 warning。fixed 缺失 base=0 已标注 ServerS4A21 代码推导，未冒称实机确认。
- Fresh check: 在工作树根设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，执行 `dotnet test Server/tests/Dfo.Server.Application.Tests --filter 'FullyQualifiedName~CharacterSkillArchiveTests|FullyQualifiedName~CharacterSkillRuleServiceTests|FullyQualifiedName~RemainingSkillPointTests|FullyQualifiedName~CharacterEntryInventoryTests|FullyQualifiedName~CharacterEntryRuleServiceTests' --logger 'trx;LogFileName=review-quick-r1-built.trx' --results-directory .rks-agent/tasks/character-rules-and-state/verification/task02`，exit 0；逐项解析 72 Passed / 0 skipped。证据 `../verification/task02/review-quick-r1-built.trx`，SHA-256 `8079D48B7D7B2C01AC9F1B3C1B6AB4E4AB6BD6FB8DE6F3498DDDD635ED7E9FEC`。先前 no-build 独立重跑同为 72 Passed，见 `review-quick-r1.trx`。相对执行者较早的 71 项定向记录，新增项为 warning 行为测试；该项也已在 application-final.trx 中通过。
- Existing evidence verified: 重新逐项解析 red-behavior.trx 为 2 Failed；green-focused.trx 为 71 Passed；application-final.trx 为 1158 Passed；infrastructure.trx 为 260 Passed / 2 NotExecuted，跳过项确为有档案时不运行的缺失档案场景。核对 build-restored.log 为构建成功、0 warnings / 0 errors；本次 `git diff --check` exit 0。
- Findings: 未发现本单元范围内可证明的阻塞缺陷。此次结论仅覆盖 TASK-02 局部检查，不表示完整 Spec 或全部消费者迁移已验收。
- Verdict: `passed`
