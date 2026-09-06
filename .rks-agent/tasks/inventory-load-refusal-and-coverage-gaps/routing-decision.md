# Routing Decision

- TaskContract: `.rks-agent/tasks/inventory-load-refusal-and-coverage-gaps/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 所有者把任务写成三条互不重叠的缺口，外加一条记载修正，并给出**同一个验收口径**（「补上缺失的拒绝与缺失的判据，正常数据上的行为逐值不变」）。目标是可观察的：判据全绿、字节锚逐字节不变、删掉生产校验会红。范围在源请求里已逐条枚举，无待定语义。
- Requirement stability: 可枚举且可评审的显式要求共十条，已落成 SC-1..SC-10：装载侧拒绝及其对堆叠物的口径、正常数据逐值不变、三条取值校验各自红于删除、时装六值搬到新行的正向覆盖、字节锚不变、全量判据读数、AGENTS.md 与代码一致、`CommandHandlerArchitectureTests` 不放宽。
- Execution direction: 中心方向已由就地复核确定——装载侧按 `row.ItemUid` 在两个读循环里去重（`InventoryStorageItem` 不带 `ItemUid`，所以只能落在循环内）；三条校验的判据落点已定位到 `InventoryDeltaLiftTests.cs` 与 `AvatarCarrierRulesTests.cs`；缺口三的正向覆盖需要构造一条 `Survivors` 三趟都不认领的搬迁，使原行被删、新行被建。
- Remaining uncertainty: 只剩非语义的局部选择——拒绝消息的措辞、新判据的命名、以及「时装珠宝槽 30 字节」那一支是否值得为它引入一个 `ICharacterItemTable` 假件（schema CHECK 使它在真库上不可达）。这些不改变任务含义，留给 Spec 与实现。

## Marginal Control Value

- User precision signal: 强烈偏精确。所有者点名要走完整链（任务契约 → 路由决策 → spec → plan → 执行 → 评审 → 验证），给了逐项目的判据基线读数、既知偶发失败的判别三要素、一条字节锚的逐字节要求、以及「每个任务单元结束时判据必须全绿，不允许攒红」。同时明确警告「不要照抄一句已被证伪的话」并要求自行复核一条记载——这是要求独立取证，不是要求速度。
- Direct failure mode: 三种都具体且已在本仓发生过。其一，**补出来的判据自己是空的**——缺口三的本质正是「一条判据靠无关的事实通过」，直接照着写很容易再造一条同样trivial 的判据（AGENTS.md 记着「缺口长的样子是固定的：把一个 handler 整段退回读陈值，全量判据仍然全绿」）。其二，**装载侧新增拒绝改变正常数据上的行为**——按 `ItemIdentity` 去重会漏掉堆叠物，按错误的粒度去重会误伤合法数据，而硬约束是逐值不变。其三，**照抄已证伪的前提**——源请求里关于「(容器, 槽) 也没检查」和「保存侧拒绝在第 145 行」两句都与代码不符，Direct 下极易把它们当事实写进实现与记载，AGENTS.md 明记本仓「已经有过好几次，一条记载漂到自己的反面还在原地躺着」。
- Useful control: Spec 把每条缺口的验收写成**可证伪的观察**（删掉生产校验必须红、令 `CarriedAvatarsAsync` 返回空表必须红、字节锚 diff 为空），使「判据自己是空的」在评审时可被当场检出；Plan 把四件事拆成各自判据全绿的执行单元，防止攒红并把「逐值不变」的核验绑在每个单元边界上；独立评审对照契约与 diff 给裁定，专门盯 Direct 失败模式其一与其三。
- Time-for-quality rationale: 逃逸代价高且不易察觉——一条 trivial 的新判据会被记进 AGENTS.md 当作「已覆盖」，正是本仓吃过亏的那种漂移（「清单把它记作『有覆盖』，而实际只覆盖了两个制造者中的一个」）；而正常数据上的行为若被改动，会在入场路径上以字节差或装载拒绝的形式伤到真实存档。相对这些，一次 Spec ＋ Plan ＋ 评审的时间成本很小，且所有者已明确要求付这个成本。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 三条缺口与一条记载修正共用**一个验收口径**、一套硬约束、一份判据、一次本地接受提交。它们改的是同一族东西（时装细节的取值与世界行的指名），彼此没有独立的所有权、集成或回滚边界——任何一条单独接受都不构成一个有意义的交付，所有者要的是这四件一起达到「拒绝与判据都补齐且行为逐值不变」。
- Why Direct is insufficient: Direct 无法便宜地提供「新判据必须可证伪」这一条控制。它要求在实现之外再做一次变异核实（删生产校验、令检索返回空表），并把结果作为验收证据留下；Direct 下这一步既无归处也无人复核，而它恰是本任务唯一能区分「补了判据」与「补了一条不会红的判据」的手段。
- Why Multi is unnecessary: 只有一个连贯的接受边界。四件事的数量与跨项目分布（Application、Protocol、Persistence、AGENTS.md）不构成 Multi 边界——它们没有各自独立的验收方、集成点或回滚点，拆成子 Spec 只会为一次提交造四份治理开销。
- Reroute conditions: 若实测发现装载侧按 `core_uid` 去重会让**既有判据或真实存档数据变红**（即「正常数据」的定义本身有争议），该口径要回到所有者，任务边界随之改变；若为覆盖「珠宝槽 30 字节」那一支需要改动 `Abstractions` 的公共契约或 schema，则超出本契约的 Out 与硬约束，须停下重路由；若缺口三的正向覆盖被证明在现有 store 语义下不可达（三趟 `Survivors` 无法构造出「原行被删」的搬迁），则该条从「补判据」变成「改设计」，属于新的任务。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `sdd-standard` 默认 Profile（Spec ＋ Plan ＋ 完整结果评审 ＋ 新鲜验证）
- Why this is sufficient: 所有者点名的链是「spec → plan → 执行 → 评审 → 验证」，与 Standard 的控制集逐项对应。本任务的三个失败模式各自被其中一项挡住：可证伪的验收写进 Spec 挡住「空判据」；按单元排序并在每个单元边界跑全量判据的 Plan 挡住攒红与「逐值不变」的漂移；独立的完整结果评审对照契约与 diff 挡住「照抄已证伪的前提」。判据本身是机器可判的（退出码 ＋ 读数 ＋ 字节锚 diff），验证环节不需要额外的取证机制。
- Why lighter is insufficient: Lite 不产出 Plan。本任务有四个改动点分布在三个项目加一份仓库级文档上，且硬约束要求**每个任务单元结束时判据全绿**——没有显式的单元切分与排序，「不允许攒红」这条约束无处落地，且变异核实（删校验、令检索返空）与还原的成对动作容易漏还原（所有者已禁用一切 git 还原类命令，还原只能靠文件级备份 ＋ 散列核对，这本身需要被排进计划）。
- Why heavier is unnecessary: Governed 的增量是独立挑战集、artifact 审批与漂移控制。本任务的验收已经是二值可判的（判据退出码、字节锚逐字节 diff、变异后必须变红），不存在需要靠对抗性挑战才能暴露的判断空间；范围由所有者逐条枚举且 Out 清单明确，漂移风险低；一次本地接受提交、不合并不推送，后果可完全本地回滚。为这些再加一层审批与挑战集，时间成本换不到对应的保护。
