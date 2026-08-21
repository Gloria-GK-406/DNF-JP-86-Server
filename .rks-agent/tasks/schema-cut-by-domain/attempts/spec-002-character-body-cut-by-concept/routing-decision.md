# Routing Decision

- TaskContract: `.rks-agent/tasks/schema-cut-by-domain/attempts/spec-002-character-body-cut-by-concept/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 设计权威已存在且已封存——`Docs/Refactor/TableOwnershipRuling.md`（提交 `9c535a9`）把六张目标表的列、来源、键、约束、默认值与建行时机全部定死，并经十轮独立评审。本成果不需要再做任何边界判断，只需照它执行。
- Requirement stability: 七条成功条件可枚举且各自可观察——目标表的列与裁定逐列比对、五张折入表不复存在、九张源表的约束逐条追到落点、六张表建角色后各有一行且八个默认值正确、下发帧字节不变、表征用例可证伪、上一轮形状未回退。
- Execution direction: 已知。上一轮（`fc6467a`）已把持久化收敛为一表一类、跨表组装归领域层，本成果在该形状上只改表的边界与列的归属。
- Remaining uncertainty: 只剩不改变成果语义的实施局部选择。裁定中标为暂定或悬置的归宿由契约明确排除在本成果之外。

## Marginal Control Value

- User precision signal: 用户指定按 multi-spec 处理，既往同类持久化任务一律选择 SDD；本轮已为设计层投入十轮评审，实施层的精度预期不低于此。
- Direct failure mode: 次序失效，且错误次序不产生红灯。三条具名的：其一，先改库形状再改建角色的写入，则建角色插入会撞上新加的 `NOT NULL`，而这一步若被「先让测试跑起来」的临时默认值掩盖，错误的默认值会一直留到实机才显形——而八个非零默认值里有五个直接或经映射到达客户端。其二，先改领域组装再改表征用例，则表征用例是照着改动后的行为重写的，它守不住任何东西；今天由它守住的列在改动中丢失不会变红。其三，九张源表的约束若在改表时逐条搬运而非先清点后核对，丢掉一条不产生任何测试信号——`character_pvp_skill_state` 的级联外键正是裁定评审中被发现即将静默丢失的那一条。
- Useful control: 一份持久的实施排序与依赖映射（Plan）把这三条次序钉死，并把「先清点约束、后改表」「先写新形状的表征用例并证明它可证伪、后改组装」定为有序任务；对完整结果的独立评审复核约束与默认值是否逐条落地。
- Time-for-quality rationale: 三条失效都不产生红灯且都在实机远离改动点处显形；本成果触及九张源表、六张目标表、约 130 列与两处千余行的组装点，事后定位成本远高于事前排序成本。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 单一连贯的接受边界。六张目标表由同一批源列构成，任何一张单独交付都会让另外几张读不到自己的列；建角色的那一次写入同时供给六张表，无法拆分；表征用例覆盖的是整条载入路径。回滚边界也是一个：库形状声明、按表接口、组装点与表征用例必须同进同退。
- Why Direct is insufficient: Direct 无法把三条不产生红灯的次序固定成可复核的顺序，而这正是本成果唯一的失效来源。
- Reroute conditions: 若实施中发现裁定的某条事实不成立且修正会改变目标形状，先更新裁定再继续，必要时退回根控制器重新裁定成果边界；若发现角色本体的改动无法与物品侧解耦（超出金币上限那一处已知耦合），退回根控制器重新划分。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-refactor`
- Why this is sufficient: Standard 的独有控制正是本成果唯一需要的那一样——一份持久的实施排序。三条失效都是「错误次序不产生红灯」，Plan 把它们钉成有序任务与逐条检查；code-refactor Profile 的 test-driven-development 与 reviewing-code 覆盖「先证明表征用例可证伪、再改组装」这条内部硬次序。
- Why lighter is insufficient: Lite 无 Plan。本成果的失效不是「忘掉某条要求」（Spec 能挡），而是「以错误次序做对的事」——Lite 的完整结果评审在三条次序失效都已发生之后才介入，那时表征用例已经照着改动后的行为重写，评审看到的是自洽的错误。
- Why heavier is unnecessary: Governed 的独有控制是独立挑战与审计强度证据，其硬性门槛本成果一条也不触及：不涉人身或系统安全、不涉凭据与访问控制、不涉隐私与受保护数据、不涉资金法务合规、不产生不可逆外部变更；目标是本机开发库且项目已定擦库不迁移，无缺乏恢复手段的持久数据丢失路径。
