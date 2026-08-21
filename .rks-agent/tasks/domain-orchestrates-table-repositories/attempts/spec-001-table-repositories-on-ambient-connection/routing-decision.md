# Routing Decision

- TaskContract: `.rks-agent/tasks/domain-orchestrates-table-repositories/attempts/spec-001-table-repositories-on-ambient-connection/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果由用户划定并在根契约固化——九个仓储退回单表读写、连接来自用例边界、组装上移且唯一。改动面已清点到表一级。
- Requirement stability: 可枚举可清点：9 个仓储、26 处自开连接、13 处独立事务、33 张表的入场装载组装、1 处表存在性探查。每一项都能在改动前后数出来。
- Execution direction: 中心方向已定且有现成机制可用——`SqliteDatabaseParticipant` 按连接串归并参与者，今天已服务两个 store，九个仓储要接的就是它。不需要新造机制。
- Remaining uncertainty: 仅剩局部选择——每张表对应的仓储命名与签名、组装落在哪个领域服务上、拆分的推进次序。均不改变任务语义。

## Marginal Control Value

- User precision signal: 用户要求「按 multispec 来」，且此前两个同类任务均选 SDD 治理流程。同时要求「全部完成后再和我说」，即接受流程成本换一次交付。
- Direct failure mode: 本子任务的主要风险是**测试全绿但目标落空**。其一，拆分时把跨表组装顺手复制到多个调用方——每处单独看都对，合起来就是存储结构泄漏到各处，且事后收回比一开始做对贵得多。其二，改接环境连接时漏掉某条路径仍自开连接——读撕裂只在并发下暴露，本地测试构造不出来。其三，33 张表的组装搬家时漏搬或改序，入场帧的字段顺序变化不一定被现有断言覆盖。
- Useful control: Spec 把「每个仓储恰好一张表」「自开连接为零」「组装唯一」写成可清点的验收条件，使前两条各有一个不依赖执行者自觉的观察点；Plan 把「先钉住今天的入场装载输出，再动组装」定为硬次序，使第三条在搬家前就有金样兜底。
- Time-for-quality rationale: 9 个仓储、26 处连接、33 张表的组装一次做完且中途不与用户交互。没有可清点的验收条件，「做完了」将无法与「大部分做完了」区分开。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 单一连贯接受边界。三件事互为前提且共享同一回滚边界——不接环境连接就按表拆，会把 26 处独立提交放大成上百处；不上移组装就按表拆，33 张表的取数过程会被迫留在某个仓储里，等于没拆；组装上移而连接未共用，则那 33 次读没有一致快照，比现在更糟。任一半单独交付都不成立。
- Why Direct is insufficient: Direct 无法提供「组装唯一」「自开连接为零」这类需跨整个改动面清点的验收条件，而这正是前两条静默失效各自的唯一观察点。
- Why Multi is unnecessary: 本子任务内部没有第二个可独立接受的结果。九个仓储虽可分批推进，但在组装上移完成之前，任何一批单独交付都留下"一半在边界内一半在边界外"的状态，不构成可接受的中间态。
- Reroute conditions: 若发现某张表的写必须与另一张表在同一语句内完成，则该仓储脱离本接受边界，交回根控制器判断归属；若按表拆分后入场装载出现可观测的延迟退化，则吞吐量假设失效，须回到用户重新确认边界。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: 需要一份持久的实施排序挡住一条不产生红灯的次序失效——入场装载的 33 张表组装若在其输出被钉住之前搬家，改序或漏搬造成的中间记录差异不一定被现有断言覆盖（整条帧序列的 SHA256 金样覆盖的是最终帧，不是 `CharacterEntryStorageRecord` 本身）。Plan 把「先表征、再搬家」定为硬次序即可挡住。此外九个仓储的拆分彼此耦合——接环境连接、按表拆、组装上移三者互为前提——需要依赖映射而非临场判断。code-refactor Profile 绑定 test-driven-development 与 reviewing-code，对应本任务行为保持的性质。
- Why lighter is insufficient: Lite 无 plan 阶段，上述硬次序只能靠执行者临场记住。该次序一旦错过，代价是在改动面已经铺开之后才发现，而中间记录的差异不会自己变红。
- Why heavier is unnecessary: 不触及人身或系统安全、凭据与访问控制、隐私与受保护数据、资金法务合规；不产生不可逆外部变更；目标是本机开发库且项目已定「开发阶段擦库不迁移」，无缺乏恢复手段的持久数据丢失路径。独立挑战与审计强度证据换不来对应的失效削减。
