# Routing Decision

- TaskContract: `.rks-agent/tasks/schema-cut-by-domain/attempts/spec-004-skills-cut-by-concept/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 设计权威已封存。裁定的 M-4 给出目标形状（七列、键、四条要携带的保证），S-4 给出为何两张职业专属页不合并，`character_skills` 的目标列表逐列写明来源。本成果不再做边界判断。
- Requirement stability: 九条成功条件可枚举且各自可观察——形状与裁定一致、`mode` 不重新解释任何列、四条保证逐条存续、收紧不改变现行行为、职业专属页仍分开且仍拒绝、播种序列行为不变、下发帧不变、守卫可证伪、上一轮形状未回退。
- Execution direction: 已知。同一套方法（形状守卫 + 表征守卫 + 规则守卫）已在 spec-002 与 spec-003 各跑通一次。
- Remaining uncertainty: 只剩不改变成果语义的实施局部选择。

## Marginal Control Value

- User precision signal: 用户指定按 multi-spec 处理；前两块的实施评审各抓到一条会到达客户端的真回归，这一层的控制已被两次证明不是形式。
- Direct failure mode: 静默丢约束与静默放宽，两者都不产生红灯。三条具名的：其一，`character_pvp_skills` 的四条保证只在它那张表上声明，`character_skills` 一条都没有；合并时按「以宽的为准」搬运会一条不剩地丢掉，而丢掉它们不产生任何测试信号——今天没有任何用例断言 PvP 技能行的 `NOT NULL` 或那条 `CHECK`。其二，反过来把四条保证加到 PvE 行上是一次**收紧**，若今天某条写 PvE 行的路径不提供其中某个值，收紧会让那条路径在实机首次写技能时才失败。其三，`PvpSkillProfileService` 是一个方法里读标记、写技能、写标记的序列，标记在 spec-002 已变成 `character_pvp.skills_initialized`；改写技能读写时打乱这个序列不会红，因为播种是否发生今天只由一条用例间接覆盖。
- Useful control: 一份持久的实施排序把这三条钉死——先把四条保证与 PvE 写入路径清点成守卫、再改表、再改读写；对完整结果的独立评审复核四条保证是否逐条落地、收紧是否真的无害。
- Time-for-quality rationale: 三条失效都不产生红灯，且第二条会在实机而非测试里显形。本成果触及两张源表、一张目标表与一处跨块的播种序列，事后定位成本远高于事前排序成本。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 单一连贯的接受边界。两张源表并成一张是一次不可分的写入——`mode` 列一旦存在，两侧的读写必须同时改，否则任一侧读到的行集合都是错的；`PvpSkillProfileService` 同时触及标记与技能，无法只改一半。回滚边界也是一个。
- Why Direct is insufficient: Direct 无法把「先清点四条保证与 PvE 写入路径、再改表」固定成可复核的顺序，而那正是本成果的失效来源。
- Reroute conditions: 若实施中发现某条写 PvE 行的路径不提供那五个列中的某一个，收紧就会改变现行行为——须先更新裁定（可能改为只按 `mode` 限定 `NOT NULL`），必要时退回根控制器。若发现 `extra_values` 在两侧语义不同，M-4 的合并须重新裁定。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: Standard 的独有控制正是本成果需要的那一样——一份持久的实施排序。三条失效都是「错误次序或漏项不产生红灯」；code-refactor Profile 的 test-driven-development 与 reviewing-code 覆盖「先立守卫、后改形状」这条内部硬次序。
- Why lighter is insufficient: Lite 无 Plan。本成果的失效不是「忘掉某条要求」，而是「搬运时静默丢掉四条只在一侧声明的约束」与「加上它们时静默收紧」；Lite 的完整结果评审在两者都已发生之后才介入。
- Why heavier is unnecessary: Governed 的独有控制是独立挑战与审计强度证据，其硬性门槛本成果一条也不触及：不涉人身或系统安全、不涉凭据与访问控制、不涉隐私与受保护数据、不涉资金法务合规、不产生不可逆外部变更；目标是本机开发库且项目已定擦库不迁移。
